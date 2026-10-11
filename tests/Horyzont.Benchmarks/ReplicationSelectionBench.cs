using System.Numerics;

using BenchmarkDotNet.Attributes;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;
using Horyzont.Server.Replication;
using Horyzont.Server.Terrain;

namespace Horyzont.Benchmarks;

/// <summary>
/// The terrain send queue of K3-16 for 8 receivers: one edited block enqueued for every receiver and taken again, as a server tick does
/// after a terrain edit seen by everybody. Enqueue keeps a <see cref="TerrainBlockData"/> message and a queue entry per receiver (104 B,
/// budget 832 B in <c>budgets.json</c>); the selection in <c>TryTake</c> allocates nothing.
/// </summary>
[MemoryDiagnoser]
public class TerrainSendQueueBench
{
    private const int Receivers = 8;
    private readonly byte[] _blob = new byte[512];
    private TerrainSendQueue _queue = null!;
    private uint _version;

    [GlobalSetup]
    public void Setup()
    {
        _queue = new TerrainSendQueue(UnlimitedBudget.Instance, TimeProvider.System.TimestampFrequency);
        for (var i = 0; i < 64; i++)
        {
            EnqueueAndTakeForEightReceivers();
        }
    }

    [Benchmark]
    public int EnqueueAndTakeForEightReceivers()
    {
        _version++;
        var key = new TerrainBlockKey(new PlanetId(1), 0, (int)(_version & 7), 250, 0);
        var taken = 0;
        for (var p = 1; p <= Receivers; p++)
        {
            var player = new PlayerId((ulong)p);
            _queue.Enqueue(player, key, _version, _blob, 0, p * 16f);
            if (_queue.TryTake(player, 0, out _))
            {
                taken++;
            }
        }

        return taken;
    }

    /// <summary>A budget that never limits, so the bench measures the queue alone.</summary>
    public sealed class UnlimitedBudget : IReplicationSendBudget
    {
        /// <summary>Gets the shared instance.</summary>
        public static UnlimitedBudget Instance { get; } = new();

        /// <inheritdoc />
        public bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes) => true;
    }
}

/// <summary>
/// Snapshot selection of K3-14/K3-15 for 8 receivers: the priority selection over 64 candidates within a byte budget, and the delta
/// snapshot builder writing into a reused buffer. Hard budget 0 B in steady state.
/// </summary>
[MemoryDiagnoser]
public class SnapshotPriorityBench
{
    private const int Receivers = 8;
    private readonly SnapshotCandidate[] _candidates = new SnapshotCandidate[64];
    private readonly int[] _selected = new int[64];
    private readonly ReplicationEntity[] _entities = new ReplicationEntity[16];
    private readonly byte[] _buffer = new byte[4096];
    private SnapshotBuilder _builder = null!;
    private uint _tick;

    [GlobalSetup]
    public void Setup()
    {
        for (var i = 0; i < _candidates.Length; i++)
        {
            _candidates[i] = new SnapshotCandidate(new EntityId((ulong)(i + 1)), i * 7.5f, 40 + (i % 5), i % 4, i == 0);
        }

        for (var i = 0; i < _entities.Length; i++)
        {
            var id = new EntityId((ulong)(100 + i));
            var frame = new SnapshotFrame(new FrameRef(FrameKind.Planet, 1), 0);
            _entities[i] = new ReplicationEntity(
                id, EntityKind.Player, new PlayerId((ulong)(i + 1)), frame,
                new EntityState(id, EntityStateFields.Position | EntityStateFields.Rotation, 0, 0, new Vector3(4000f, i, 0f), Quaternion.Identity, Vector3.Zero, 0),
                0f);
        }

        _builder = new SnapshotBuilder(new FixedSnapshotBudget());
        for (var i = 0; i < 64; i++)
        {
            BuildForEightReceivers();
        }
    }

    [Benchmark]
    public int SelectForEightReceivers()
    {
        var total = 0;
        for (var p = 0; p < Receivers; p++)
        {
            total += SnapshotSchedulingPolicy.Select(_candidates, _selected, 1150 - (p * 50));
        }

        return total;
    }

    [Benchmark]
    public int BuildForEightReceivers()
    {
        _tick++;
        var bytes = 0;
        for (var p = 1; p <= Receivers; p++)
        {
            var player = new PlayerId((ulong)p);
            _builder.SetEntities(player, _entities);
            var written = _builder.Build(player, new Tick(_tick), _tick, _buffer);
            bytes += written;
            _builder.Acknowledge(player, _tick);
        }

        return bytes;
    }
}
