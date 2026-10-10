using System.Numerics;

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
}
