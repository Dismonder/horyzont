using Horyzont.Client.Session;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Replication;

/// <summary>Reconstructs acknowledged wire deltas before delivering complete snapshots to movement consumers.</summary>
public sealed class SnapshotDecoder : IClientSessionListener
{
    private readonly Snapshot?[] _history = new Snapshot[64];
    private readonly List<IFullSnapshotSink> _sinks = [];
    private readonly Action<uint>? _ack;
    private uint _latest;
    private bool _hasLatest;

    /// <summary>Creates a decoder with an optional acknowledgement sender.</summary>
    /// <param name="ack">Called only after successful reconstruction.</param>
    public SnapshotDecoder(Action<uint>? ack = null) => _ack = ack;

    /// <summary>Registers a consumer of reconstructed snapshots.</summary>
    /// <param name="sink">Consumer.</param>
    public void AddSink(IFullSnapshotSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (!_sinks.Contains(sink))
        {
            _sinks.Add(sink);
        }
    }

    /// <summary>Reconstructs a delta using exactly its retained baseline. Missing entities need all fields.</summary>
    /// <param name="delta">Wire snapshot.</param>
    /// <param name="full">Complete states of the entities present in this packet.</param>
    /// <returns>Whether reconstruction succeeded.</returns>
    public bool TryReconstruct(Snapshot delta, out Snapshot full)
    {
        ArgumentNullException.ThrowIfNull(delta);
        full = null!;
        if (_hasLatest && unchecked((int)(delta.SnapshotId - _latest)) <= 0)
        {
            return false;
        }

        Snapshot? baseline = null;
        if (delta.BaselineSnapshotId != 0)
        {
            baseline = _history[delta.BaselineSnapshotId % 64];
            if (baseline is null || baseline.SnapshotId != delta.BaselineSnapshotId)
            {
                return false;
            }
        }

        var states = new EntityState[delta.Entities.Count];
        for (var i = 0; i < states.Length; i++)
        {
            var state = delta.Entities[i];
            if (state.FrameIndex >= delta.Frames.Count)
            {
                return false;
            }

            for (var j = 0; j < i; j++)
            {
                if (states[j].Entity == state.Entity)
                {
                    return false;
                }
            }

            if (state.Fields != (EntityStateFields)15)
            {
                var found = false;
                if (baseline is not null)
                {
                    foreach (var previous in baseline.Entities)
                    {
                        if (previous.Entity != state.Entity || previous.ParentSeq != state.ParentSeq
                            || baseline.Frames[previous.FrameIndex] != delta.Frames[state.FrameIndex])
                        {
                            continue;
                        }

                        state = state with
                        {
                            Position = state.Fields.HasFlag(EntityStateFields.Position) ? state.Position : previous.Position,
                            Rotation = state.Fields.HasFlag(EntityStateFields.Rotation) ? state.Rotation : previous.Rotation,
                            Velocity = state.Fields.HasFlag(EntityStateFields.Velocity) ? state.Velocity : previous.Velocity,
                            MotionFlags = state.Fields.HasFlag(EntityStateFields.MotionFlags) ? state.MotionFlags : previous.MotionFlags,
                            Fields = (EntityStateFields)15,
                        };
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            states[i] = state;
        }

        full = new Snapshot(delta.SnapshotId, delta.ServerTick, 0, delta.LastProcessedInputSeq, delta.Frames, states);
        _history[delta.SnapshotId % 64] = full;
        _latest = delta.SnapshotId;
        _hasLatest = true;
        return true;
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        if (message is Snapshot delta && TryReconstruct(delta, out var full))
        {
            _ack?.Invoke(full.SnapshotId);
            foreach (var sink in _sinks)
            {
                sink.OnFullSnapshot(full);
            }
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        Array.Clear(_history);
        _hasLatest = false;
    }
}
