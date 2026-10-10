using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Server.Replication;

namespace Horyzont.Server.Tests.Replication;

public sealed class SnapshotBuilderTests
{
    [Fact]
    public void Snapshot_TooManyEntities_Respects1150BytesAndEventuallySendsAll()
    {
        var builder = new SnapshotBuilder(new FixedSnapshotBudget());
        var player = new PlayerId(1);
        var entities = new ReplicationEntity[40];
        for (var i = 0; i < entities.Length; i++)
        {
            var id = new EntityId((ulong)i + 1);
            entities[i] = new ReplicationEntity(id, EntityKind.Player, i == 0 ? player : null,
                new SnapshotFrame(FrameRef.Of(new PlanetId((ulong)(i % 10) + 1)), 0),
                new EntityState(id, (EntityStateFields)15, 0, 0, Vector3.One, Quaternion.Identity, Vector3.Zero, 0), 0);
        }

        builder.SetEntities(player, entities);
        var seen = new HashSet<EntityId>();
        var bytes = new byte[1150];
        for (ulong tick = 0; tick < 12; tick++)
        {
            var size = builder.Build(player, new Tick(tick), 0, bytes);
            Assert.InRange(size, 1, 1150);
            var snapshot = Assert.IsType<Snapshot>(MessageCodec.Read(bytes.AsSpan(0, size)));
            Assert.Equal(entities[0].Id, snapshot.Entities[0].Entity);
            Assert.InRange(snapshot.Frames.Count, 1, 8);
            foreach (var state in snapshot.Entities)
            {
                seen.Add(state.Entity);
            }
        }

        Assert.Equal(40, seen.Count);
    }

    [Fact]
    public void BuildSnapshot_WarmedUp10000Calls_AllocatesZeroBytes()
    {
        var builder = new SnapshotBuilder(new FixedSnapshotBudget());
        var player = new PlayerId(1);
        var id = new EntityId(1);
        builder.SetEntities(player, [new(id, EntityKind.Player, player, new(FrameRef.Of(new PlanetId(1)), 0),
            new(id, (EntityStateFields)15, 0, 0, Vector3.One, Quaternion.Identity, Vector3.Zero, 0), 0)]);
        var bytes = new byte[1150];
        for (ulong i = 0; i < 100; i++)
        {
            builder.Build(player, new Tick(i), 0, bytes);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong i = 0; i < 10000; i++)
        {
            builder.Build(player, new Tick(i), 0, bytes);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static ReplicationEntity Body(ulong id, PlayerId? owner, Vector3 position, byte flags = 0) =>
        new(new EntityId(id), EntityKind.Player, owner, new SnapshotFrame(FrameRef.Of(new PlanetId(1)), 0),
            new EntityState(new EntityId(id), (EntityStateFields)15, 0, 0, position, Quaternion.Identity, Vector3.Zero, flags), 0);

    [Fact]
    public void Build_AcknowledgedBaseline_SendsDeltaAndEveryTwentiethFull()
    {
        var builder = new SnapshotBuilder(new FixedSnapshotBudget());
        var player = new PlayerId(1);
        ReplicationEntity[] entities = [Body(1, player, Vector3.One), Body(2, null, Vector3.UnitX)];
        builder.SetEntities(player, entities);
        var bytes = new byte[1150];
        var fullSize = builder.Build(player, new Tick(1), 0, bytes);
        Assert.False(builder.Acknowledge(player, 99));
        Assert.False(builder.Acknowledge(player, 0));
        Assert.True(builder.Acknowledge(player, 1));
        Assert.False(builder.Acknowledge(player, 1));

        // Nothing changed for the other body: its record shrinks to the header; the own body is always sent in full.
        var deltaSize = builder.Build(player, new Tick(4), 0, bytes);
        var delta = Assert.IsType<Snapshot>(MessageCodec.Read(bytes.AsSpan(0, deltaSize)));
        Assert.Equal(1u, delta.BaselineSnapshotId);
        Assert.Equal((EntityStateFields)15, delta.Entities[0].Fields);
        Assert.Equal(EntityStateFields.None, delta.Entities[1].Fields);
        Assert.True(deltaSize < fullSize);

        // A baseline that was never sent, or already overwritten in the ring, is ignored.
        for (uint tick = 5; tick < 22; tick++)
        {
            var size = builder.Build(player, new Tick(tick), 0, bytes);
            builder.Acknowledge(player, tick - 3 + 1);
            var snapshot = Assert.IsType<Snapshot>(MessageCodec.Read(bytes.AsSpan(0, size)));
            if (snapshot.SnapshotId % 20 == 1)
            {
                Assert.Equal(0u, snapshot.BaselineSnapshotId);
            }
        }
    }

    [Fact]
    public void Snapshot_RandomLossAndStaleAcknowledgements_DecoderAlwaysMatchesServerState()
    {
        var builder = new SnapshotBuilder(new FixedSnapshotBudget());
        var decoder = new SnapshotDecoder();
        var player = new PlayerId(1);
        var random = new Random(77);
        var entities = new ReplicationEntity[12];
        for (var i = 0; i < entities.Length; i++)
        {
            entities[i] = Body((ulong)i + 1, i == 0 ? player : null, new Vector3(i, 0, 0));
        }

        var bytes = new byte[1150];
        var reconstructed = 0;
        var deltas = 0;
        var pendingAcks = new Queue<uint>();
        for (uint n = 1; n <= 600; n++)
        {
            for (var i = 0; i < entities.Length; i++)
            {
                if (random.Next(10) < 3)
                {
                    var e = entities[i];
                    entities[i] = e with { State = e.State with { Position = e.State.Position + new Vector3(0.5f, 0, 0), MotionFlags = (byte)random.Next(3) } };
                }
            }

            // Entities come and go: the set shrinks and grows, so a returning body needs full fields again.
            var visible = entities.Where((_, i) => i == 0 || ((n / 25) + i) % 5 != 0).ToArray();
            builder.SetEntities(player, visible);
            var size = builder.Build(player, new Tick(n), n, bytes);
            Assert.InRange(size, 1, 1150);
            if (random.Next(100) < 20)
            {
                continue;
            }

            var wire = Assert.IsType<Snapshot>(MessageCodec.Read(bytes.AsSpan(0, size)));
            deltas += wire.BaselineSnapshotId == 0 ? 0 : 1;
            if (decoder.TryReconstruct(wire, out var full))
            {
                reconstructed++;
                Assert.Equal(0u, full.BaselineSnapshotId);
                foreach (var state in full.Entities)
                {
                    var truth = visible.Single(e => e.Id == state.Entity).State;
                    Assert.Equal(truth.Position, state.Position);
                    Assert.Equal(truth.MotionFlags, state.MotionFlags);
                }

                pendingAcks.Enqueue(full.SnapshotId);
            }

            // Acknowledgements arrive late, get lost, or repeat an old id.
            if (pendingAcks.Count > 2 && random.Next(100) < 70)
            {
                builder.Acknowledge(player, pendingAcks.Dequeue());
            }

            if (random.Next(100) < 5)
            {
                builder.Acknowledge(player, (uint)random.Next(1, 600));
            }
        }

        Assert.True(reconstructed > 300);
        Assert.True(deltas > 100, "deltas must be used: " + deltas);
    }
}
