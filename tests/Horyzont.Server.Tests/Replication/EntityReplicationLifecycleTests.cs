using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Server.Replication;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Replication;

public sealed class EntityReplicationLifecycleTests
{
    [Fact]
    public async Task Replicate_HostLocal_UsesOneConstructWorldAndTerrain()
    {
        await using var rig = await ReplicationRig.StartAsync();
        // A client named "local" connects from the runtime's local endpoint: the host's own client shares the world.
        var host = await rig.JoinAsync("local", 1);
        var remote = await rig.JoinAsync("remote", 2, new Vector3(20, 4000, 0));
        Assert.True(rig.Runtime.IsLocalPlayer(host.Player));
        var construct = rig.AddConstruct(new Vector3(5, 4000, 5));
        rig.Step(9);
        rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(construct, new CellPos(1, 0, 0), ReplicationRig.Stone));
        rig.Step(6);

        Assert.Contains(host.Received, m => m is EntitySpawn s && s.Entity == rig.EntityOf(remote.Player));
        Assert.DoesNotContain(host.Received, m => m is ConstructState or OperationApplied or ConstructRemoved);
        Assert.DoesNotContain(host.Received, m => m is EntitySpawn { Kind: EntityKind.Construct });
        Assert.Contains(remote.Received, m => m is ConstructState);
        Assert.Contains(remote.Received, m => m is OperationApplied);
        Assert.Contains(host.Received, m => m is Snapshot);
    }

    [Fact]
    public async Task Shutdown_ReplicationActive_DrainsAndReleasesOnce()
    {
        var budget = new GatedSendBudget { Open = false };
        await using var rig = await ReplicationRig.StartAsync(sendBudget: budget);
        var alice = await rig.JoinAsync("alice", 1);
        var construct = rig.AddConstruct(new Vector3(5, 4000, 5), new CellPos(40, 0, 0));
        rig.Step(9);
        rig.Runtime.Simulation.ApplyServerOperation(new BlockSetOp(construct, new CellPos(1, 0, 0), ReplicationRig.Stone));
        rig.Step(3);
        var replication = rig.Replication;
        Assert.True(replication.Constructs.IsTransferring(alice.Player, construct));
        Assert.True(replication.Constructs.StagedBytes(alice.Player) > 0);

        await rig.Rig.StopAsync();
        Assert.Equal(0, replication.TrackedPlayers);
        Assert.Equal(0, replication.Constructs.StagedBytes(alice.Player));
        Assert.Equal(0, replication.Constructs.BufferedOperations(alice.Player));
        var received = alice.Received.Count;

        // Detaching again and ticking a released replication neither throws nor sends.
        replication.Detach();
        replication.Step(new Tick(300));
        Assert.Equal(received, alice.Received.Count);
    }

    [Fact]
    public async Task Replicate_PlayerLeaves_OthersGetOneDespawnAndNoStateLeaks()
    {
        await using var rig = await ReplicationRig.StartAsync();
        var alice = await rig.JoinAsync("alice", 1);
        var bob = await rig.JoinAsync("bob", 2, new Vector3(20, 4000, 0));
        rig.Step(9);
        var bobBody = rig.EntityOf(bob.Player);
        Assert.True(rig.Replication.Knows(alice.Player, bobBody));
        bob.Session.Disconnect();
        rig.Step(120);
        Assert.Equal(1, alice.Received.OfType<EntityDespawn>().Count(m => m.Entity == bobBody));
        Assert.False(rig.Replication.Knows(alice.Player, bobBody));
        Assert.Equal(1, rig.Replication.TrackedPlayers);
    }

    [Fact]
    public void Step_SteadyStateEightPlayers_AllocatesNothing()
    {
        using var transport = new CopyingTransport();
        var simulation = new ServerSimulation(WorldFactory.CreateDefault(), new FakeTerrainService(), new InMemoryJournal(),
            new GameRules(), NullLogger<ServerSimulation>.Instance);
        var time = new FakeTimeProvider();
        var session = new ServerSession(transport, new ServerOptions { BuildHash = 1 },
            new GameServerHooks(simulation, () => true, NullLogger<GameServerHooks>.Instance), time);
        session.Poll(new Tick(1));
        Assert.Equal(8, session.PlayersInGame.Count);
        var source = new MovingSource(simulation.State, session.PlayersInGame);
        var replication = new EntityReplication(session, simulation, source, new FixedSnapshotBudget(), new TokenBucketReplicationBudget(time),
            time, NullLogger<EntityReplication>.Instance);
        for (ulong tick = 0; tick < 600; tick++)
        {
            source.Move();
            replication.Step(new Tick(tick));
        }

        var sent = transport.SnapshotsSent;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 600; tick < 10_600; tick++)
        {
            source.Move();
            replication.Step(new Tick(tick));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.InRange(transport.SnapshotsSent - sent, (8 * (10_000 / 3)) - 16, (8 * (10_000 / 3)) + 16);
    }

    private sealed class MovingSource(Horyzont.Core.World.WorldState world, IReadOnlyList<PlayerId> players) : IEntityReplicationSource
    {
        private float _x;

        public void Move() => _x += 0.001f;

        public int CopyEntities(Span<ReplicationEntity> destination)
        {
            var count = 0;
            for (var i = 0; i < players.Count && count < destination.Length; i++)
            {
                world.Players.TryGet(players[i], out var player);
                var state = new EntityState(player!.Entity, (EntityStateFields)15, 0, 0, new Vector3(_x + i, 4000, 0), Quaternion.Identity,
                    Vector3.UnitX, 1);
                destination[count++] = new ReplicationEntity(player.Entity, EntityKind.Player, players[i],
                    new SnapshotFrame(FrameRef.Of(WorldFactory.HomePlanet), 0), state, 0f);
            }

            destination[..count].Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
            return count;
        }

        public uint GetLastProcessedInputSeq(PlayerId player) => 0;
    }

    private sealed class CopyingTransport : ITransport
    {
        private readonly byte[] _copy = new byte[ProtocolConstants.MaxReliableMessageBytes];
        private bool _joined;

        public int SnapshotsSent { get; private set; }

        public int MaxReliablePayload => ProtocolConstants.MaxReliableMessageBytes;

        public int MaxUnreliablePayload => ProtocolConstants.MaxUnreliableMessageBytes;

        public bool IsDrained => true;

        public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
        {
            payload.CopyTo(_copy);
            if (payload[0] == (byte)MessageType.Snapshot)
            {
                SnapshotsSent++;
            }
        }

        public void Poll(ITransportEvents events)
        {
            if (_joined)
            {
                return;
            }

            _joined = true;
            var writer = new ByteWriter();
            for (ulong id = 1; id <= 8; id++)
            {
                var connection = new ConnectionId(id);
                events.OnConnected(connection);
                writer.Reset();
                MessageCodec.Write(writer, new ClientHello(ProtocolConstants.ProtocolVersion, 1,
                    new PlatformIdentity(IdentityPlatform.Dev, id), "player", "", []));
                events.OnReceived(connection, TransportChannel.ReliableOrdered, writer.WrittenSpan);
                writer.Reset();
                MessageCodec.Write(writer, new ClientReady());
                events.OnReceived(connection, TransportChannel.ReliableOrdered, writer.WrittenSpan);
            }
        }

        public void Listen(TransportEndpoint endpoint)
        {
        }

        public ConnectionId Connect(TransportEndpoint endpoint) => throw new NotSupportedException();

        public void Disconnect(ConnectionId connection)
        {
        }

        public TransportStats GetStats(ConnectionId connection) => default;

        public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) => new InMemoryEndpoint("client");

        public void Dispose()
        {
        }
    }
}
