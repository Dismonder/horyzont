using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Server.Replication;
using Horyzont.Server.Tests.Support;

namespace Horyzont.Server.Tests.Replication;

public sealed class ReplicationConvergenceTests
{
    private static ulong Mix(ulong hash, ulong value)
    {
        hash ^= value;
        return hash * 1099511628211UL;
    }

    private static ulong Digest(Construct construct)
    {
        var hash = 14695981039346656037UL;
        hash = Mix(hash, construct.Id.Value);
        hash = Mix(hash, ((ulong)construct.Parent.Kind << 56) ^ construct.Parent.Id);
        hash = Mix(hash, construct.ParentSeq);
        hash = Mix(hash, construct.Version);
        foreach (var component in (ReadOnlySpan<float>)[construct.Origin.X, construct.Origin.Y, construct.Origin.Z,
            construct.Rotation.X, construct.Rotation.Y, construct.Rotation.Z, construct.Rotation.W])
        {
            hash = Mix(hash, BitConverter.SingleToUInt32Bits(component));
        }

        foreach (var pair in construct.Chunks)
        {
            hash = Mix(hash, (ulong)(uint)pair.Key.X ^ ((ulong)(uint)pair.Key.Y << 20) ^ ((ulong)(uint)pair.Key.Z << 40));
            foreach (var block in pair.Value.Blocks)
            {
                hash = Mix(hash, block);
            }
        }

        return hash;
    }

    /// <summary>Stands for a transfer that the shared budget stretches over many seconds: one bulk part per second.</summary>
    private sealed class OnePartPerSecondBudget(TimeProvider time) : IReplicationSendBudget
    {
        private long _last = long.MinValue;

        public bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes)
        {
            if (traffic == ReplicationTraffic.Live)
            {
                return true;
            }

            var now = time.GetTimestamp();
            if (_last != long.MinValue && time.GetElapsedTime(_last, now) < TimeSpan.FromSeconds(1))
            {
                return false;
            }

            _last = now;
            return true;
        }
    }

    private sealed class Mirrored
    {
        public Mirrored(RigClient client, TimeProvider time)
        {
            Client = client;
            Replication = new ClientReplication(message =>
            {
                if (message is ResyncRequest)
                {
                    Resyncs++;
                }

                client.Session.Send(message);
            }, new ConstructMirror(time));
            client.Observer = Replication.OnMessage;
        }

        public RigClient Client { get; }

        public ClientReplication Replication { get; }

        public int Resyncs { get; private set; }

        public ConstructMirror Constructs => Replication.Constructs!;
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Replicate_TwoClientsRandomEditsTenMinutes_ConstructDigestsEqual()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = new Mirrored(await rig.JoinAsync("alice", 1), rig.Time);
        var bob = new Mirrored(await rig.JoinAsync("bob", 2, new Vector3(30, 4000, 0)), rig.Time);
        Vector3[] origins = [new(10, 4000, 10), new(-20, 4000, 15), new(5, 4000, -25)];
        var ids = origins.Select(origin => rig.AddConstruct(origin)).ToArray();
        var rng = new Random(1515);
        var simulation = rig.Runtime.Simulation;
        var compared = 0;
        var removals = 0;

        for (var tick = 1; tick <= 36_000; tick++)
        {
            if (tick % 6 == 0)
            {
                var which = rng.Next(ids.Length);
                if (!rig.Runtime.State.Constructs.TryGet(ids[which], out var target))
                {
                    ids[which] = rig.AddConstruct(origins[which]);
                }
                else if (tick % 900 == 0)
                {
                    // Empty the construct block by block: the last clear removes it.
                    var cells = target.Chunks.SelectMany(c => Cells(c.Key, c.Value)).ToList();
                    foreach (var cell in cells)
                    {
                        simulation.ApplyServerOperation(new BlockClearOp(target.Id, cell));
                    }

                    removals++;
                }
                else if (rng.Next(100) < 40 && target.BlockCount > 1)
                {
                    var cells = target.Chunks.SelectMany(c => Cells(c.Key, c.Value)).ToList();
                    simulation.ApplyServerOperation(new BlockClearOp(target.Id, cells[rng.Next(cells.Count)]));
                }
                else
                {
                    var cell = new CellPos(rng.Next(0, 40), rng.Next(0, 20), rng.Next(0, 4));
                    if (target.GetBlock(cell).IsEmpty)
                    {
                        simulation.ApplyServerOperation(new BlockSetOp(target.Id, cell, ReplicationRig.Stone));
                    }
                }
            }

            rig.Step();
            if (tick % 60 == 0)
            {
                compared += Compare(rig, alice) + Compare(rig, bob);
            }
        }

        rig.Step(60);
        Assert.Equal(0, alice.Resyncs + bob.Resyncs);
        Assert.True(removals >= 30, "the run must remove constructs: " + removals);
        foreach (var mirrored in new[] { alice, bob })
        {
            Assert.Equal(rig.Runtime.State.Constructs.Count, mirrored.Constructs.Store.Count);
            foreach (var construct in rig.Runtime.State.Constructs.All)
            {
                Assert.True(mirrored.Constructs.TryGetConstruct(construct.Id, out var copy), "missing construct " + construct.Id.Value);
                Assert.Equal(Digest(construct), Digest(copy));
            }
        }

        Assert.InRange(compared, 1000, int.MaxValue);
    }

    private static IEnumerable<CellPos> Cells(ChunkPos chunk, ConstructChunk data)
    {
        for (var i = 0; i < ConstructChunk.CellCount; i++)
        {
            if (!data.Get(i).IsEmpty)
            {
                yield return CellPos.FromChunk(chunk, i);
            }
        }
    }

    private static int Compare(ReplicationRig rig, Mirrored mirrored)
    {
        var compared = 0;
        foreach (var construct in rig.Runtime.State.Constructs.All)
        {
            if (rig.Replication.Constructs.IsLive(mirrored.Client.Player, construct.Id))
            {
                Assert.True(mirrored.Constructs.TryGetConstruct(construct.Id, out var copy), "live construct missing on the client");
                Assert.Equal(Digest(construct), Digest(copy));
                compared++;
            }
        }

        return compared;
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Replicate_Rtt100Jitter10_DeliveryLeavesRoomForMeshingWithin200Milliseconds()
    {
        var link = new LinkConditions(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), 0.05, 0, 7);
        await using var rig = await ReplicationRig.StartAsync(conditions: link);
        var alice = new Mirrored(await rig.JoinAsync("alice", 1), rig.Time);
        var id = rig.AddConstruct(new Vector3(10, 4000, 10));
        rig.Step(120);
        Assert.True(alice.Constructs.TryGetConstruct(id, out _));

        var sentAt = new Dictionary<OpId, DateTimeOffset>();
        var delays = new List<double>();
        alice.Constructs.OperationObserved += op =>
        {
            if (sentAt.TryGetValue(op, out var at))
            {
                delays.Add((rig.Time.GetUtcNow() - at).TotalMilliseconds);
            }
        };
        for (var i = 1; i <= 200; i++)
        {
            var cell = new CellPos(i % 40, i / 40, 0);
            var applied = rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(id, cell, ReplicationRig.Stone));
            sentAt[applied.Id] = rig.Time.GetUtcNow();
            rig.Step(10);
        }

        rig.Step(30);
        Assert.Equal(200, delays.Count);
        delays.Sort();
        var p99 = delays[(int)(delays.Count * 0.99)];
        // Report: delivery of an edit (the engine adds receive -> mesh and collision swap, measured by the gdUnit scenario).
        Assert.InRange(delays[^1], 0, 130);
        Assert.InRange(p99, 0, 130);
        Assert.Equal(0, alice.Resyncs);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Replicate_LargeConstruct_DoesNotBlockTicksAndArrivesComplete()
    {
        var gate = new GatedSendBudget();
        await using var rig = await ReplicationRig.StartAsync(sendBudget: gate);
        var alice = new Mirrored(await rig.JoinAsync("alice", 1), rig.Time);
        var cells = new CellPos[300];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = new CellPos((16 * (i % 20)) + 1, 16 * (i / 20), 0);
        }

        var id = rig.AddConstruct(new Vector3(10, 4000, 10), cells);

        // Hold bulk traffic while the construct is captured and planned. Tick and snapshot progress must
        // not depend on finishing the transfer, regardless of scheduler pauses or concurrent test load.
        gate.Open = false;
        alice.Client.Received.Clear();
        var before = rig.Runtime.State.CurrentTick.Value;
        rig.Step(120);
        Assert.True(rig.Runtime.State.CurrentTick.Value >= before + 100);
        Assert.True(rig.Replication.Constructs.StagedBytes(alice.Client.Player) > 0);
        Assert.False(alice.Constructs.TryGetConstruct(id, out _));
        Assert.Equal(0, gate.BulkBytes);
        var snapshots = alice.Client.Received.OfType<Snapshot>().ToArray();
        Assert.True(snapshots.Length >= 30, "Own-entity snapshots must continue while bulk traffic is blocked.");
        Assert.All(snapshots, snapshot => Assert.Equal(rig.EntityOf(alice.Client.Player), snapshot.Entities[0].Entity));
        for (var i = 1; i < snapshots.Length; i++)
        {
            Assert.Equal(3ul, snapshots[i].ServerTick.Value - snapshots[i - 1].ServerTick.Value);
        }

        gate.Open = true;
        var ticks = 0;
        while (ticks < 1200 && !alice.Constructs.TryGetConstruct(id, out _))
        {
            var messagesBefore = alice.Client.Received.Count;
            var tickBefore = rig.Runtime.State.CurrentTick.Value;
            rig.Step();
            var parts = alice.Client.Received.Skip(messagesBefore).OfType<ConstructState>().Count();
            var advanced = rig.Runtime.State.CurrentTick.Value - tickBefore;
            Assert.InRange((ulong)parts, 0ul, advanced * ConstructStateSender.MaxPartsPerPump);
            ticks++;
        }

        rig.Step(10);
        Assert.True(alice.Constructs.TryGetConstruct(id, out var copy));
        Assert.Equal(Digest(rig.Runtime.State.Constructs.All.Single(c => c.Id == id)), Digest(copy!));
        Assert.Equal(0, alice.Resyncs);
        Assert.Equal(0, rig.Replication.Constructs.StagedBytes(alice.Client.Player));
        Assert.True(gate.BulkBytes > 0);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Replicate_SlowLargeConstructUnderSharedBudget_IsPublishedWithoutDisconnect()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        await using var rig = await ReplicationRig.StartAsync(time: time, sendBudget: new OnePartPerSecondBudget(time));
        var alice = new Mirrored(await rig.JoinAsync("alice", 1), rig.Time);
        var cells = new CellPos[300];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = new CellPos((16 * (i % 20)) + 1, 16 * (i / 20), 0);
        }

        var id = rig.AddConstruct(new Vector3(10, 4000, 10), cells);
        var ticks = 0;
        while (ticks < 60 * 120 && !alice.Constructs.TryGetConstruct(id, out _))
        {
            rig.Step();
            alice.Constructs.ExpireTransfers();
            alice.Replication.Entities.RetryResyncs();
            ticks++;
        }

        Assert.True(ticks > 5 * 60, "the transfer must outlast the former 5 s deadline: " + ticks);
        Assert.True(alice.Constructs.TryGetConstruct(id, out var copy), "the copy was never published");
        Assert.Equal(Digest(rig.Runtime.State.Constructs.All.Single(c => c.Id == id)), Digest(copy!));
        Assert.Equal(0, alice.Resyncs);
        Assert.Equal(0, alice.Constructs.StagingBytes);
    }
}
