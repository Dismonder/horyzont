using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Server.Replication;

namespace Horyzont.Server.Ships;

/// <summary>
/// K5-25 decision 15: the server state of the ship a player pilots after the step of input slot
/// <see cref="PilotControlSource.TryGetLastProcessedSeq"/>, from <see cref="ShipFlightSystem.TryGetView"/> (motion after the step,
/// environment of the step). No correction while the flight or the pilot slots are not ready.
/// </summary>
/// <param name="simulation">The server simulation.</param>
/// <param name="flight">The flight (created on the first tick of a world).</param>
/// <param name="pilot">The pilot at the controls.</param>
public sealed class ShipCorrectionSender(ServerSimulation simulation, Func<ShipFlightSystem?> flight, Func<PilotControlSource?> pilot) : IShipCorrectionSource
{
    /// <summary>Gets the number of corrections written.</summary>
    public long Written { get; private set; }

    /// <inheritdoc />
    public int Write(PlayerId player, Span<byte> destination, out EntityId ship)
    {
        ship = default;
        var state = simulation.State;
        if (flight() is not { } system || pilot() is not { } source || !state.Players.TryGet(player, out var seated)
            || seated.Seat is null || seated.Frame.Kind != FrameKind.Construct)
        {
            return 0;
        }

        var id = new EntityId(seated.Frame.Id);
        if (!source.TryGetLastProcessedSeq(id, out var pilotPlayer, out var seq) || pilotPlayer != player
            || !system.TryGetView(id, out var view) || !state.Constructs.TryGet(id, out var construct) || view.Parent != construct.Parent
            || !FrameMath.TryToFloat(view.Motion.Pose, view.Parent.Kind, out var position, out var rotation))
        {
            return 0;
        }

        var motion = view.Motion;
        var environment = view.Environment;
        ship = id;
        Written++;
        return ShipCorrectionWriter.Write(
            destination, id, seq, construct.ParentSeq, position, rotation, motion.LinearVelocity, motion.AngularVelocity, motion.Fuel,
            motion.Flags, environment.Gate, environment.LaunchBlock, (float)environment.HorizontalSpeedCap);
    }
}
