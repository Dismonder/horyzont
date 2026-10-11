using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;
using Horyzont.Server.Replication;
using Horyzont.Server.Terrain;

namespace Horyzont.Server.Tests.Replication;

/// <summary>
/// The allocation budgets of <c>TerrainSendQueueBench</c> and <c>SnapshotPriorityBench</c> (K4-19), checked deterministically here so
/// that the hard budgets of <c>tests/Horyzont.Benchmarks/budgets.json</c> are known before a nightly run.
/// </summary>
public sealed class ReplicationSelectionAllocationTests
{
    private const int Receivers = 8;

    [Fact]
    public void TerrainSendQueue_SteadyState_TakeAllocatesNothing_EnqueueOnlyItsMessage()
    {
        var queue = new TerrainSendQueue(new Unlimited(), TimeProvider.System.TimestampFrequency);
        var blob = new byte[512];
        uint version = 0;
        long enqueued = 0;
        long took = 0;
        var taken = 0;
        void Round()
        {
            version++;
            var key = new TerrainBlockKey(new PlanetId(1), 0, (int)(version & 7), 250, 0);
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var p = 1; p <= Receivers; p++)
            {
                queue.Enqueue(new PlayerId((ulong)p), key, version, blob, 0, p * 16f);
            }

            var middle = GC.GetAllocatedBytesForCurrentThread();
            for (var p = 1; p <= Receivers; p++)
            {
                taken += queue.TryTake(new PlayerId((ulong)p), 0, out _) ? 1 : 0;
            }

            enqueued += middle - start;
            took += GC.GetAllocatedBytesForCurrentThread() - middle;
        }

        for (var i = 0; i < 64; i++)
        {
            Round();
        }

        (enqueued, took, taken) = (0, 0, 0);
        for (var i = 0; i < 100; i++)
        {
            Round();
        }

        Assert.Equal(100 * Receivers, taken);
        Assert.Equal(0, took);

        // Enqueue keeps a TerrainBlockData message and its queue entry (two records, 104 B on x64): 832 B per round of 8 receivers,
        // the budget of TerrainSendQueueBench in budgets.json.
        Assert.True(enqueued / 100.0 <= Receivers * 104, $"{enqueued / 100.0} B per round of 8 enqueues");
    }

    [Fact]
    public void SnapshotSelectionAndBuild_SteadyState_AllocateNothing()
    {
        var candidates = new SnapshotCandidate[64];
        for (var i = 0; i < candidates.Length; i++)
        {
            candidates[i] = new SnapshotCandidate(new EntityId((ulong)(i + 1)), i * 7.5f, 40 + (i % 5), i % 4, i == 0);
        }

        var entities = new ReplicationEntity[16];
        for (var i = 0; i < entities.Length; i++)
        {
            var id = new EntityId((ulong)(100 + i));
            entities[i] = new ReplicationEntity(
                id, EntityKind.Player, new PlayerId((ulong)(i + 1)), new SnapshotFrame(new FrameRef(FrameKind.Planet, 1), 0),
                new EntityState(id, EntityStateFields.Position | EntityStateFields.Rotation, 0, 0, new Vector3(4000f, i, 0f), Quaternion.Identity, Vector3.Zero, 0),
                0f);
        }

        var selected = new int[64];
        var buffer = new byte[4096];
        var builder = new SnapshotBuilder(new FixedSnapshotBudget());
        uint tick = 0;
        var bytes = 0;
        void Round()
        {
            tick++;
            for (var p = 1; p <= Receivers; p++)
            {
                SnapshotSchedulingPolicy.Select(candidates, selected, 1150 - (p * 50));
                var player = new PlayerId((ulong)p);
                builder.SetEntities(player, entities);
                bytes += builder.Build(player, new Tick(tick), tick, buffer);
                builder.Acknowledge(player, tick);
            }
        }

        for (var i = 0; i < 64; i++)
        {
            Round();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Round();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes > 0);
        Assert.Equal(0, allocated);
    }

    private sealed class Unlimited : IReplicationSendBudget
    {
        public bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes) => true;
    }
}
