using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Ships;
using Horyzont.Core.Simulation;

namespace Horyzont.Server.Frames;

/// <summary>
/// The velocities of ejections (K5-25 decision 19, R4): <see cref="EntityReparentOp.LocalVelocity"/> is not stored by Core, so every
/// reparent into a planet frame with a velocity above <see cref="EjectRules.GlideMinSpeed"/> is remembered by entity and reparent
/// counter until the server body takes it (<see cref="TryTake"/>) or <see cref="ExpireTicks"/> pass.
/// </summary>
public sealed class EjectVelocities : IDisposable
{
    /// <summary>Ticks an untaken velocity is kept (2 s at 60 Hz).</summary>
    public const int ExpireTicks = 120;

    private readonly ServerSimulation _simulation;
    private readonly Dictionary<EntityId, Pending> _pending = [];
    private readonly List<EntityId> _expired = [];

    /// <summary>Subscribes to <see cref="ServerSimulation.Applied"/>.</summary>
    /// <param name="simulation">The simulation.</param>
    public EjectVelocities(ServerSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        _simulation = simulation;
        simulation.Applied += OnApplied;
    }

    /// <summary>Gets the number of remembered velocities.</summary>
    public int Count => _pending.Count;

    /// <summary>Takes the velocity of the reparent <paramref name="parentSeq"/> of <paramref name="entity"/>.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="parentSeq">Its reparent counter after the operation.</param>
    /// <param name="velocity">The velocity in the new frame; zero when none.</param>
    /// <returns>True when the reparent had an ejection velocity.</returns>
    public bool TryTake(EntityId entity, uint parentSeq, out Vector3 velocity)
    {
        Expire();
        if (_pending.Remove(entity, out var pending) && pending.ParentSeq == parentSeq)
        {
            velocity = pending.Velocity;
            return true;
        }

        velocity = Vector3.Zero;
        return false;
    }

    /// <inheritdoc />
    public void Dispose() => _simulation.Applied -= OnApplied;

    private void OnApplied(AppliedOperation applied)
    {
        if (applied.Operation is EntityReparentOp op && op.NewParent.Kind == FrameKind.Planet && op.LocalVelocity.Length() > EjectRules.GlideMinSpeed)
        {
            _pending[op.Entity] = new Pending(op.NewParentSeq, op.LocalVelocity, applied.Tick.Value);
        }
    }

    private void Expire()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var now = _simulation.State.CurrentTick.Value;
        foreach (var pair in _pending)
        {
            if (now - pair.Value.Tick > ExpireTicks)
            {
                _expired.Add(pair.Key);
            }
        }

        foreach (var id in _expired)
        {
            _pending.Remove(id);
        }

        _expired.Clear();
    }

    private readonly record struct Pending(uint ParentSeq, Vector3 Velocity, ulong Tick);
}
