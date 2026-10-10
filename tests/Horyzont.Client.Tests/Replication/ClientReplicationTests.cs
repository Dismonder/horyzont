using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Replication;

public sealed class ClientReplicationTests
{
    private static readonly SnapshotFrame _frame = new(FrameRef.Of(new PlanetId(1)), 0);

    private static EntityState State(ulong entity, uint parentSeq = 0, float x = 1, EntityStateFields fields = (EntityStateFields)15) =>
        new(new EntityId(entity), fields, 0, parentSeq, new Vector3(x, 0, 0), Quaternion.Identity, Vector3.Zero, 0);

    private static EntitySpawn Spawn(ulong entity, uint parentSeq = 0) =>
        new(new EntityId(entity), EntityKind.Player, 0, new PlayerId(entity), _frame, State(entity, parentSeq), []);

    private sealed class Sink : IFullSnapshotSink
    {
        public List<Snapshot> Snapshots { get; } = [];

        public void OnFullSnapshot(Snapshot full) => Snapshots.Add(full);
    }

    [Fact]
    public void OnMessage_DeltaSnapshots_AcknowledgesOnlyReconstructedAndFeedsSinks()
    {
        var sent = new List<Message>();
        var replication = new ClientReplication(sent.Add, null);
        var sink = new Sink();
        replication.Decoder.AddSink(sink);
        replication.OnMessage(Spawn(2));
        replication.OnMessage(new Snapshot(1, new Tick(3), 0, 0, [_frame], [State(2, x: 5)]));
        replication.OnMessage(new Snapshot(2, new Tick(6), 1, 0, [_frame], [State(2, x: 6, fields: EntityStateFields.Position)]));
        // Unknown baseline: no acknowledgement and no sink call.
        replication.OnMessage(new Snapshot(3, new Tick(9), 77, 0, [_frame], [State(2, x: 7, fields: EntityStateFields.Position)]));
        Assert.Equal([1u, 2u], sent.OfType<SnapshotAck>().Select(a => a.SnapshotId).ToArray());
        Assert.Equal(2, sink.Snapshots.Count);
        Assert.All(sink.Snapshots, s => Assert.Equal(0u, s.BaselineSnapshotId));
        Assert.True(replication.Entities.TryGet(new EntityId(2), out var spawn));
        Assert.Equal(6f, spawn!.Initial.Position.X);
    }

    [Fact]
    public void OnMessage_LifecycleAndConstructMessages_ReachMirrors()
    {
        var sent = new List<Message>();
        var time = new FakeTimeProvider();
        var replication = new ClientReplication(sent.Add, new ConstructMirror(time));
        var id = new EntityId(9);
        var header = new ConstructHeaderRecord(id, FrameRef.Of(new PlanetId(1)), 0, ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 1);
        replication.OnMessage(new ConstructState(header, 0, 1, []));
        Assert.True(replication.Constructs!.TryGetConstruct(id, out _));
        replication.OnMessage(new ConstructRemoved(id));
        Assert.False(replication.Constructs.TryGetConstruct(id, out _));

        // A version gap asks for exactly one repair.
        replication.OnMessage(new ConstructState(header, 0, 1, []));
        var gap = new OperationApplied(new OpId(5), default, new Horyzont.Core.Operations.BlockClearOp(id, default), [new(id, 7, false)]);
        replication.OnMessage(gap);
        replication.OnMessage(gap);
        Assert.Equal([id], sent.OfType<ResyncRequest>().Select(r => r.Entity).ToArray());

        replication.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.HostLeft);
        Assert.False(replication.Constructs.TryGetConstruct(id, out _));
        Assert.Equal(0, replication.Entities.Count);
    }

    [Fact]
    public void EntityMirror_ParentSeqAheadOrEntityUnknown_ParksUntilSpawnOrReparentThenResyncs()
    {
        var mirror = new EntityMirror();
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;

        // Samples before the spawn wait for it.
        mirror.OnFullSnapshot(new Snapshot(1, new Tick(1), 0, 0, [_frame], [State(3, x: 9)]));
        Assert.Equal(1, mirror.ParkedSamples);
        mirror.Apply(Spawn(3));
        Assert.True(mirror.TryGet(new EntityId(3), out var known));
        Assert.Equal(9f, known!.Initial.Position.X);
        Assert.Equal(0, mirror.ParkedSamples);

        // A newer counter waits for the reparent; an older one is dropped.
        mirror.OnFullSnapshot(new Snapshot(2, new Tick(2), 0, 0, [_frame], [State(3, parentSeq: 1, x: 20)]));
        mirror.OnFullSnapshot(new Snapshot(3, new Tick(3), 0, 0, [_frame], [State(3, parentSeq: uint.MaxValue, x: 99)]));
        Assert.Equal(1, mirror.ParkedSamples);
        mirror.Apply(new OperationApplied(new OpId(1), default,
            new Horyzont.Core.Operations.EntityReparentOp(new EntityId(3), FrameRef.Of(new PlanetId(1)), 1, Vector3.Zero, Quaternion.Identity, Vector3.Zero, null), []));
        mirror.TryGet(new EntityId(3), out known);
        Assert.Equal(20f, known!.Initial.Position.X);

        // More than 32 parked samples ask for one resynchronisation.
        for (uint i = 0; i < 40; i++)
        {
            mirror.OnFullSnapshot(new Snapshot(10 + i, new Tick(10 + i), 0, 0, [_frame], [State(3, parentSeq: 5, x: i)]));
        }

        Assert.Single(requests);
        Assert.True(mirror.ParkedSamples <= EntityMirror.MaxParkedSamples);
    }
}
