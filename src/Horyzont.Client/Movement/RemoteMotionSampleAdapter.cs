using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Movement;

/// <summary>Feeds registered remote rings from the single reconstructed snapshot path. Lifecycle belongs to K3-15.</summary>
public sealed class RemoteMotionSampleAdapter : IFullSnapshotSink, IClientSessionListener
{
    private readonly Dictionary<EntityId, RemoteMotionBuffer> _buffers = [];
    private EntityId _own;
    private bool _connected;

    /// <summary>Creates the full snapshot sink and its session clock.</summary>
    /// <param name="time">Monotonic session time.</param>
    public RemoteMotionSampleAdapter(TimeProvider time) { Clock = new RemoteMotionPolicy(time); }

    /// <summary>Gets the single remote interpolation clock.</summary>
    public RemoteMotionPolicy Clock { get; }

    /// <summary>Gets the total number of sample underruns in active remote rings.</summary>
    public long SampleUnderruns
    {
        get { long count = 0; foreach (var buffer in _buffers.Values) { count += buffer.SampleUnderruns; } return count; }
    }

    /// <summary>Registers a ring when K3-15 spawns a remote player.</summary>
    /// <param name="entity">Remote player entity.</param>
    /// <param name="buffer">Fresh ring owned by its presentation.</param>
    public void Register(EntityId entity, RemoteMotionBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (entity == _own) { throw new ArgumentException("The local body cannot have a remote ring.", nameof(entity)); }
        if (_buffers.TryGetValue(entity, out var previous)) { previous.Clear(); }
        buffer.Clear();
        _buffers[entity] = buffer;
    }

    /// <summary>Forgets the ring before K3-15 removes a remote view.</summary>
    /// <param name="entity">Despawned entity.</param>
    public void Remove(EntityId entity) { if (_buffers.Remove(entity, out var buffer)) { buffer.Clear(); } }

    /// <summary>Extracts complete finite remote states without treating absent fields as zero.</summary>
    /// <param name="full">Semantically full snapshot with baseline zero.</param>
    /// <param name="own">Own entity to omit.</param>
    /// <param name="into">Caller-owned sample storage.</param>
    /// <returns>Number of valid samples written.</returns>
    public static int Extract(Snapshot full, EntityId own, Span<RemoteMotionSample> into)
    {
        ArgumentNullException.ThrowIfNull(full);
        if (full.BaselineSnapshotId != 0) { return 0; }
        var count = 0;
        foreach (var entity in full.Entities)
        {
            if (count == into.Length) { break; }
            if (entity.Entity == own || (entity.Fields & (EntityStateFields)15) != (EntityStateFields)15 || entity.FrameIndex >= full.Frames.Count) { continue; }
            var position = entity.Position;
            var velocity = entity.Velocity;
            var length = entity.Rotation.Length();
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)
                || !float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z)
                || !float.IsFinite(length) || MathF.Abs(length - 1f) > 0.01f) { continue; }
            into[count++] = new RemoteMotionSample(entity.Entity, full.Frames[entity.FrameIndex].Frame, entity.ParentSeq,
                full.ServerTick, position, System.Numerics.Quaternion.Normalize(entity.Rotation), velocity, entity.MotionFlags);
        }
        return count;
    }

    /// <inheritdoc />
    public void OnFullSnapshot(Snapshot full)
    {
        if (!_connected || full.BaselineSnapshotId != 0) { return; }
        if (Clock.Observe(full.ServerTick)) { foreach (var buffer in _buffers.Values) { buffer.Clear(); } }
        Span<RemoteMotionSample> samples = stackalloc RemoteMotionSample[ProtocolConstants.MaxEntitiesPerSnapshot];
        var count = Extract(full, _own, samples);
        for (var i = 0; i < count; i++)
        {
            if (_buffers.TryGetValue(samples[i].Entity, out var buffer)) { buffer.Push(samples[i]); }
        }
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        Clear();
        _own = hello.YourEntity;
        Clock.Start(hello.ServerTick, hello.TickRate);
        _connected = true;
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) { Clear(); }

    private void Clear()
    {
        foreach (var buffer in _buffers.Values) { buffer.Clear(); }
        _buffers.Clear();
        Clock.Reset();
        _connected = false;
    }
}
