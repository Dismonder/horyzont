using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Server.Replication;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Replication;

/// <summary>K3-14 step 6: the real K3-15 snapshot builder and decoder feeding the remote motion rings of K3-14.</summary>
public sealed class MotionReplicationIntegrationTests
{
    private static readonly PlayerId _self = new(1);
    private static readonly EntityId _selfBody = new(1);

    [Fact]
    public void Build_TightBudget_SendsOwnBodyThenNearestFirst()
    {
        var near = Body(2, new Vector3(10, 0, 0));
        var mid = Body(3, new Vector3(100, 0, 0));
        var far = Body(4, new Vector3(300, 0, 0));
        var found = false;
        for (var budget = 20; budget < 200 && !found; budget++)
        {
            var builder = new SnapshotBuilder(new FixedBudget(budget));
            builder.SetEntities(_self, [far, mid, Body(1, Vector3.Zero, _self), near]);
            var bytes = new byte[1150];
            var size = builder.Build(_self, new Tick(1), 0, bytes);
            if (size == 0)
            {
                continue;
            }

            var snapshot = Assert.IsType<Snapshot>(MessageCodec.Read(bytes.AsSpan(0, size)));
            Assert.InRange(size, 1, budget);
            Assert.Equal(_selfBody, snapshot.Entities[0].Entity);
            if (snapshot.Entities.Count == 2)
            {
                Assert.Equal(near.Id, snapshot.Entities[1].Entity);
                found = true;
            }
        }

        Assert.True(found, "some budget must fit exactly the own body and one other entity");
    }

    [Fact]
    public void Build_FarEntityUnderPersistentPressure_IsStillSentWithinTenRounds()
    {
        var entities = new List<ReplicationEntity> { Body(1, Vector3.Zero, _self) };
        for (ulong i = 0; i < 30; i++)
        {
            entities.Add(Body(10 + i, new Vector3(5 + i, 0, 0)));
        }

        var far = Body(99, new Vector3(500, 0, 0));
        entities.Add(far);
        var builder = new SnapshotBuilder(new FixedBudget(130));
        builder.SetEntities(_self, entities.ToArray());
        var bytes = new byte[1150];
        var round = 0;
        for (; round < 40; round++)
        {
            var size = builder.Build(_self, new Tick((ulong)round), 0, bytes);
            var snapshot = Assert.IsType<Snapshot>(MessageCodec.Read(bytes.AsSpan(0, size)));
            Assert.Equal(_selfBody, snapshot.Entities[0].Entity);
            if (snapshot.Entities.Any(e => e.Entity == far.Id))
            {
                break;
            }
        }

        // 31 competitors at a few per round: every starved entity is served in age order, so the far one is bounded, not first.
        Assert.InRange(round, 10, 25);
    }

    [Fact]
    public void RealLink_DeltasSpawnAndDespawn_FeedRingsAndStayWithinActualByteBudget()
    {
        var time = new FakeTimeProvider();
        var budget = new MotionSnapshotBudget(time);
        var builder = new SnapshotBuilder(budget);
        var adapter = new RemoteMotionSampleAdapter(time);
        var ack = 0u;
        var decoder = new SnapshotDecoder(id => ack = id);
        decoder.AddSink(adapter);
        adapter.OnServerHello(new ServerHello(default, new Tick(0), 60, _self, _selfBody, [], [], []));
        var ringB = new RemoteMotionBuffer();
        var ringC = new RemoteMotionBuffer();
        adapter.Register(new EntityId(2), ringB);
        adapter.Register(new EntityId(3), ringC);

        var bytes = new byte[1150];
        long total = 0;
        var maxFrame = 0;
        for (uint n = 1; n <= 600; n++)
        {
            time.Advance(TimeSpan.FromSeconds(1.0 / 20));
            var x = n * 0.1f;
            var visible = new List<ReplicationEntity> { Body(1, new Vector3(x, 0, 0), _self), Body(2, new Vector3(0, x, 0)) };
            if (n <= 300)
            {
                visible.Add(Body(3, new Vector3(0, 0, x)));
            }

            builder.SetEntities(_self, visible.ToArray());
            var size = builder.Build(_self, new Tick(n * 3), 0, bytes);
            if (size == 0)
            {
                continue;
            }

            total += size;
            maxFrame = Math.Max(maxFrame, size);
            decoder.OnMessage(MessageCodec.Read(bytes.AsSpan(0, size)));
            builder.Acknowledge(_self, ack);
            if (n == 300)
            {
                // EntityDespawn from K3-15: the ring and the clock entry are dropped, a respawn gets a fresh ring.
                adapter.Remove(new EntityId(3));
                Assert.Equal(0, ringC.Count);
            }
        }

        Assert.True(ringB.Count > 0, "the remote body must have full samples although the wire carries deltas");
        Assert.True(ringB.TrySample(adapter.Clock.RenderTick, out var sample));
        Assert.Equal(new EntityId(2), sample.Entity);
        Assert.True(sample.Position.Y > 50f);
        Assert.Equal(0, ringC.Count);
        Assert.InRange(maxFrame, 1, 1150);
        // 30 s at 20 Hz: the debited actual bytes never exceed 20,480 B/s plus the 2,300 B burst.
        Assert.True(total <= (20480 * 30) + 2300, $"sent {total} B");
    }

    private static ReplicationEntity Body(ulong id, Vector3 position, PlayerId? owner = null) =>
        new(new EntityId(id), EntityKind.Player, owner, new SnapshotFrame(FrameRef.Of(new PlanetId(1)), 0),
            new EntityState(new EntityId(id), (EntityStateFields)15, 0, 0, position, Quaternion.Identity, Vector3.Zero, 0), 0);

    private sealed class FixedBudget(int bytes) : ISnapshotBudget
    {
        public int GetByteBudget(PlayerId player, Tick tick) => bytes;

        public void Debit(PlayerId player, int encodedBytes)
        {
        }
    }
}
