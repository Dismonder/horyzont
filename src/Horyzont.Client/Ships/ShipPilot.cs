using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Ships;

/// <summary>
/// The pilot loop of the client (K5-25 decisions 11, 16, 18): while the local player sits in the control cockpit of a ship, every tick
/// numbers the control (<see cref="ShipInputSequencer"/>), steps <see cref="ShipPrediction"/> with it (the ship responds in this very
/// tick) and sends the batch; corrections of the ship reconcile the prediction (the first one starts it); a reparent of the ship rebases
/// it. <see cref="PoseOverride"/> draws the own ship in the prediction. Allocation-free per tick.
/// </summary>
public sealed class ShipPilot
{
    private readonly ShipPrediction _prediction;
    private readonly ShipInputSequencer _sequencer = new();
    private readonly Action<uint, IReadOnlyList<ShipInput>> _send;
    private readonly PilotShipPose _pose;
    private ShipStats? _stats;

    /// <summary>Creates the loop.</summary>
    /// <param name="prediction">The prediction.</param>
    /// <param name="send">Sends one batch (encode at once — the list is reused).</param>
    public ShipPilot(ShipPrediction prediction, Action<uint, IReadOnlyList<ShipInput>> send)
    {
        _prediction = prediction ?? throw new ArgumentNullException(nameof(prediction));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _pose = new PilotShipPose(prediction);
    }

    /// <summary>Gets the ship the local player pilots; default when none.</summary>
    public EntityId Ship { get; private set; }

    /// <summary>Gets the prediction.</summary>
    public ShipPrediction Prediction => _prediction;

    /// <summary>Gets the pose override of the own ship (R7).</summary>
    public IClientShipPoseOverride PoseOverride => _pose;

    /// <summary>Gets the pilot keys and their button states.</summary>
    public ShipPilotInput Input { get; } = new();

    /// <summary>Gets the number of sent batches.</summary>
    public long Batches { get; private set; }

    /// <summary>Takes the controls of <paramref name="ship"/> (or keeps them); a new ship starts a new numbering and prediction.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="stats">Its stats (client mirror, bitwise equal to the server).</param>
    public void Begin(EntityId ship, ShipStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (ship != Ship)
        {
            End();
            Ship = ship;
            _pose.Ship = ship;
        }

        _stats = stats;
    }

    /// <summary>Leaves the controls.</summary>
    public void End()
    {
        if (Ship == default)
        {
            return;
        }

        Ship = default;
        _pose.Ship = default;
        _stats = null;
        _sequencer.Reset();
        _prediction.Clear();
        Input.Reset();
    }

    /// <summary>One client tick of the pilot.</summary>
    /// <param name="clientTick">Client tick.</param>
    /// <param name="control">The control of the tick.</param>
    public void Tick(uint clientTick, in ShipControl control)
    {
        if (Ship == default)
        {
            return;
        }

        var seq = _sequencer.Record(clientTick, control);
        _prediction.Step(seq, control);
        while (_sequencer.TryTakeBatch(out var first, out var inputs))
        {
            _send(first, inputs);
            Batches++;
        }
    }

    /// <summary>A correction from the server.</summary>
    /// <param name="correction">The correction.</param>
    public void OnCorrection(ShipCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(correction);
        if (Ship == default || correction.Ship != Ship || _stats is null)
        {
            return;
        }

        if (!_prediction.HasState)
        {
            _prediction.Reset(correction, _stats);
            return;
        }

        _prediction.Apply(correction);
    }

    /// <summary>
    /// The reparent operation of the piloted ship arrived (zone transition): rebases the prediction by the origin of the old frame seen
    /// from the new one at the tick of the operation, which applies a held correction of the new frame; without both frame poses the
    /// prediction stops and the next correction starts it again.
    /// </summary>
    /// <param name="op">The reparent operation (ignored for another entity).</param>
    /// <param name="oldParent">The frame the ship was in.</param>
    /// <param name="history">Frame poses by tick; null before the frames arrive.</param>
    /// <param name="tick">Server tick of the operation.</param>
    /// <returns>True when the prediction was rebased.</returns>
    public bool OnShipReparent(EntityReparentOp op, FrameRef oldParent, IFrameHistoryView? history, ulong tick)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (op.Entity != Ship || Ship == default)
        {
            return false;
        }

        if (history is not null && history.TryGetWorldPose(oldParent, tick, out var from) && history.TryGetWorldPose(op.NewParent, tick, out var to))
        {
            // EA frames share their axes: the old origin seen from the new frame is a translation.
            OnShipReparent(op.Entity, op.NewParentSeq, from.Position - to.Position);
            return true;
        }

        _prediction.Clear();
        return false;
    }

    /// <summary>The ship was reparented (zone transition).</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="parentSeq">Its new reparent counter.</param>
    /// <param name="offset">Origin of the old frame in the new frame.</param>
    public void OnShipReparent(EntityId ship, uint parentSeq, in Vector3D offset)
    {
        if (ship == Ship && ship != default)
        {
            _prediction.Rebase(parentSeq, offset);
        }
    }
}
