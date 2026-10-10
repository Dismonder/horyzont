using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Game.Server.Characters;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests;

public sealed class OwnStateSenderTests
{
    [Fact]
    public void Send_EightPlayersAfterWarmup_AllocatesNothing()
    {
        using var transport = new CopyingTransport();
        var simulation = new ServerSimulation(WorldFactory.CreateDefault(), new FakeTerrainService(), new InMemoryJournal(),
            new GameRules(), NullLogger<ServerSimulation>.Instance);
        var session = new ServerSession(transport, new ServerOptions { BuildHash = 1 },
            new GameServerHooks(simulation, () => true, NullLogger<GameServerHooks>.Instance), new FakeTimeProvider());
        session.Poll(new Tick(1));
        Assert.Equal(8, session.PlayersInGame.Count);
        var source = new CharacterSource();
        var sender = new OwnStateSender(session, simulation, source);
        for (ulong tick = 0; tick < 600; tick++)
        {
            sender.Send(new Tick(tick));
        }

        var sent = transport.SnapshotsSent;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 600; tick < 1200; tick++)
        {
            sender.Send(new Tick(tick));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1600, transport.SnapshotsSent - sent);
        Assert.Equal(0, allocated);
        var last = Assert.IsType<Snapshot>(MessageCodec.Read(transport.LastFrame));
        Assert.Equal(400u, last.SnapshotId);
        Assert.Equal(new Tick(1197), last.ServerTick);
        Assert.Equal(source.State.LastProcessedInputSeq, last.LastProcessedInputSeq);
        Assert.Equal(source.State.Feet, last.Entities[0].Position);
        Assert.Equal(source.State.Frame, last.Frames[0].Frame);
        simulation.State.Players.TryGet(session.PlayersInGame[^1], out var player);
        const EntityStateFields fields = EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.Velocity | EntityStateFields.MotionFlags;
        var canonical = new ByteWriter();
        MessageCodec.Write(canonical, new Snapshot(last.SnapshotId, last.ServerTick, 0, last.LastProcessedInputSeq,
            [new SnapshotFrame(source.State.Frame, source.State.ParentSeq)],
            [new EntityState(player!.Entity, fields, 0, source.State.ParentSeq, source.State.Feet,
                source.State.BodyRotation, source.State.Velocity, (byte)source.State.Mode)]));
        Assert.Equal(canonical.WrittenSpan.ToArray(), transport.LastFrame.ToArray());
        source.State = source.State with
        {
            ParentSeq = uint.MaxValue,
            LastProcessedInputSeq = uint.MaxValue,
            Feet = new Vector3(-2, 4007, 11),
            BodyRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f),
            Velocity = new Vector3(1.25f, -2.5f, 3.5f),
            Mode = MovementMode.Falling,
        };
        sender.Send(new Tick(ulong.MaxValue));
        canonical.Reset();
        MessageCodec.Write(canonical, new Snapshot(401, new Tick(ulong.MaxValue), 0, uint.MaxValue,
            [new SnapshotFrame(source.State.Frame, uint.MaxValue)],
            [new EntityState(player.Entity, fields, 0, uint.MaxValue, source.State.Feet,
                Quaternion.Normalize(source.State.BodyRotation), source.State.Velocity, (byte)source.State.Mode)]));
        Assert.Equal(canonical.WrittenSpan.ToArray(), transport.LastFrame.ToArray());
        sender.Forget(session.PlayersInGame[^1]);
        sender.Send(new Tick(1200));
        Assert.Equal(1u, Assert.IsType<Snapshot>(MessageCodec.Read(transport.LastFrame)).SnapshotId);
    }

    [Fact]
    public void SendEncoded_InvalidRoutingHeaderOrSize_RejectsFrame()
    {
        using var transport = new CopyingTransport();
        var simulation = new ServerSimulation(WorldFactory.CreateDefault(), new FakeTerrainService(), new InMemoryJournal(),
            new GameRules(), NullLogger<ServerSimulation>.Instance);
        var session = new ServerSession(transport, new ServerOptions { BuildHash = 1 },
            new GameServerHooks(simulation, () => true, NullLogger<GameServerHooks>.Instance), new FakeTimeProvider());
        Assert.Throws<ArgumentException>(() => session.SendEncoded(PlayerId.None, MessageType.ClientHello, [(byte)MessageType.ClientHello, 1]));
        Assert.Throws<ArgumentException>(() => session.SendEncoded(PlayerId.None, MessageType.Snapshot, [(byte)MessageType.Ping, 1]));
        Assert.Throws<ArgumentException>(() => session.SendEncoded(PlayerId.None, MessageType.Snapshot, [(byte)MessageType.Snapshot]));
        var oversized = new byte[ProtocolConstants.MaxUnreliableMessageBytes + 1];
        oversized[0] = (byte)MessageType.Snapshot;
        oversized[1] = 1;
        Assert.Throws<ArgumentException>(() => session.SendEncoded(PlayerId.None, MessageType.Snapshot, oversized));
    }

    private sealed class CharacterSource : IServerCharacterSource
    {
        internal ServerCharacterState State { get; set; } = new(WorldFactory.HomePlanet, FrameRef.Of(WorldFactory.HomePlanet), 2,
            new Vector3(2, 4006, 3), Quaternion.Identity, 0, Vector3.Zero, MovementMode.Walking, 1234);

        public bool TryGetState(PlayerId player, out ServerCharacterState state)
        {
            state = State;
            return true;
        }
    }

    // The transport copies synchronously, like ITransport requires, but reuses its storage. No outgoing queues or decoder
    // allocations contaminate the measurement of the real sender, ServerSession and encoding path.
    private sealed class CopyingTransport : ITransport
    {
        private readonly byte[] _copy = new byte[ProtocolConstants.MaxReliableMessageBytes];
        private int _length;
        private bool _joined;

        public int SnapshotsSent { get; private set; }

        public ReadOnlySpan<byte> LastFrame => _copy.AsSpan(0, _length);

        public int MaxReliablePayload => ProtocolConstants.MaxReliableMessageBytes;

        public int MaxUnreliablePayload => ProtocolConstants.MaxUnreliableMessageBytes;

        public bool IsDrained => true;

        public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
        {
            payload.CopyTo(_copy);
            _length = payload.Length;
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

        public void Listen(TransportEndpoint endpoint) { }

        public ConnectionId Connect(TransportEndpoint endpoint) => throw new NotSupportedException();

        public void Disconnect(ConnectionId connection) { }

        public TransportStats GetStats(ConnectionId connection) => default;

        public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) => new InMemoryEndpoint("client");

        public void Dispose() { }
    }
}
