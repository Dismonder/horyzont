using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Protocol;

public sealed class ProtocolContractTests
{
    private static void AssertValues<T>(params (T Value, int Number)[] expected)
        where T : struct, Enum
    {
        Assert.Equal(expected.Length, Enum.GetValues<T>().Length);
        foreach (var (value, number) in expected)
        {
            Assert.Equal(number, Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Enums_GoldenValues()
    {
        AssertValues((OperationKind.TerrainEdit, 1), (OperationKind.ConstructCreate, 2), (OperationKind.BlockSet, 3), (OperationKind.BlockClear, 4),
            (OperationKind.PlayerJoin, 5), (OperationKind.PlayerLeave, 6), (OperationKind.EntityReparent, 7));
        AssertValues((RejectReason.None, 0), (RejectReason.UnknownPlayer, 1), (RejectReason.PlayerOffline, 2), (RejectReason.RateLimited, 3),
            (RejectReason.InvalidParameters, 4), (RejectReason.RayOriginMismatch, 5), (RejectReason.NoHit, 6), (RejectReason.OutOfReach, 7),
            (RejectReason.AreaNotLoaded, 8), (RejectReason.BelowCore, 9), (RejectReason.OutsideAtmosphere, 10), (RejectReason.UnknownPlanet, 11),
            (RejectReason.UnknownConstruct, 12), (RejectReason.CellOutOfBounds, 13), (RejectReason.CellOccupied, 14), (RejectReason.CellEmpty, 15),
            (RejectReason.UnknownBlockType, 16), (RejectReason.UnknownMaterial, 17), (RejectReason.IntersectsPlayer, 18),
            (RejectReason.NoLineOfSight, 19), (RejectReason.WrongPlanet, 20), (RejectReason.ServerReadOnly, 21));
        AssertValues((MessageType.ClientHello, 1), (MessageType.ServerHello, 2), (MessageType.HandshakeReject, 3), (MessageType.ClientReady, 4),
            (MessageType.TerrainCacheReport, 5), (MessageType.TerrainBlockData, 6), (MessageType.TerrainChecksums, 7), (MessageType.ConstructState, 8),
            (MessageType.ConstructRemoved, 9), (MessageType.OperationApplied, 10), (MessageType.IntentSubmit, 11), (MessageType.IntentReply, 12),
            (MessageType.InputBatch, 13), (MessageType.Snapshot, 14), (MessageType.SnapshotAck, 15), (MessageType.EntitySpawn, 16),
            (MessageType.EntityDespawn, 17), (MessageType.Ping, 18), (MessageType.Pong, 19), (MessageType.Disconnect, 20));
        AssertValues((DeliveryChannel.ReliableOrdered, 0), (DeliveryChannel.Unreliable, 1));
        AssertValues((HandshakeRejectCode.WrongProtocol, 1), (HandshakeRejectCode.WrongBuild, 2), (HandshakeRejectCode.ServerFull, 3),
            (HandshakeRejectCode.BadPassword, 4), (HandshakeRejectCode.Banned, 5), (HandshakeRejectCode.AlreadyConnected, 6),
            (HandshakeRejectCode.IdentityMismatch, 7));
        AssertValues((DisconnectCode.Normal, 0), (DisconnectCode.Timeout, 1), (DisconnectCode.ProtocolViolation, 2), (DisconnectCode.Kicked, 3),
            (DisconnectCode.ServerShutdown, 4), (DisconnectCode.HostLeft, 5), (DisconnectCode.HandshakeTimeout, 6));
        AssertValues((IdentityPlatform.Dev, 1), (IdentityPlatform.Steam, 2));
        AssertValues((FrameKind.System, 1), (FrameKind.Planet, 2), (FrameKind.Construct, 3));
        AssertValues((Face.PosX, 0), (Face.NegX, 1), (Face.PosY, 2), (Face.NegY, 3), (Face.PosZ, 4), (Face.NegZ, 5));
        AssertValues((ConstructMode.Static, 0), (ConstructMode.Ship, 1));
        AssertValues((BlockShape.Cube, 1), (BlockShape.Mesh, 2));
        AssertValues((BlobKind.ConstructHeader, 1), (BlobKind.Player, 2), (BlobKind.ConstructChunk, 3), (BlobKind.PlanetParams, 4),
            (BlobKind.TerrainBlock, 5));
        AssertValues((TerrainShape.Sphere, 1));
        AssertValues((TerrainEditMode.Remove, 1), (TerrainEditMode.Add, 2));
        AssertValues((EntityKind.Player, 1), (EntityKind.Construct, 2));
        AssertValues((EntityStateFields.None, 0), (EntityStateFields.Position, 1), (EntityStateFields.Rotation, 2), (EntityStateFields.Velocity, 4),
            (EntityStateFields.MotionFlags, 8));
        AssertValues((InputButtons.None, 0), (InputButtons.Jump, 1), (InputButtons.Sprint, 2), (InputButtons.Crouch, 4), (InputButtons.Primary, 8),
            (InputButtons.Secondary, 16), (InputButtons.DevFly, 32));
    }

    /// <summary>
    /// K2-10 adds reject reason 21 (and raised the protocol version from 1 to 2; K2-11 raises it to 3, see <see cref="ProtocolVersion_IsThree"/>).
    /// </summary>
    [Fact]
    public void RejectReason_ServerReadOnly_RoundTrips()
    {
        var reply = new IntentReply(7, RejectReason.ServerReadOnly, OpId.None);
        var writer = new ByteWriter();

        MessageCodec.Write(writer, reply);
        var decoded = MessageCodec.Read(writer.WrittenSpan);

        Assert.Equal(reply, decoded);
        Assert.Equal(21, (byte)RejectReason.ServerReadOnly);
        var beyond = writer.WrittenSpan.ToArray();
        beyond[^2] = 22;
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(beyond));
    }

    /// <summary>
    /// Gate decision D4: the value on main plus one. K2-10 raised it from 1 to 2; K2-11 adds <see cref="InputButtons.DevFly"/> and
    /// the 0x3F button mask and raises it to 3. A rebase onto a newer main updates this value, the constant and the K2-11 plan together.
    /// </summary>
    [Fact]
    public void ProtocolVersion_IsThree()
    {
        Assert.Equal(3, ProtocolConstants.ProtocolVersion);
        Assert.Equal(0x3F, ProtocolConstants.InputButtonsMask);
    }

    [Fact]
    public void Constants_GoldenValues()
    {
        Assert.Equal(3, ProtocolConstants.ProtocolVersion);
        Assert.Equal(262144, ProtocolConstants.MaxReliableMessageBytes);
        Assert.Equal(1150, ProtocolConstants.MaxUnreliableMessageBytes);
        Assert.Equal(32, ProtocolConstants.MaxDisplayNameBytes);
        Assert.Equal(64, ProtocolConstants.MaxRegistryKeyBytes);
        Assert.Equal(2048, ProtocolConstants.MaxBlockTypes);
        Assert.Equal(255, ProtocolConstants.MaxMaterials);
        Assert.Equal(22, ProtocolConstants.MaxEntitiesPerSnapshot);
        Assert.Equal(8, ProtocolConstants.MaxFramesPerSnapshot);
    }

    [Fact]
    public void MessageChannels_MapsEveryType()
    {
        var unreliable = new[] { MessageType.InputBatch, MessageType.Snapshot, MessageType.SnapshotAck, MessageType.Ping, MessageType.Pong };
        foreach (var type in Enum.GetValues<MessageType>())
        {
            var expected = unreliable.Contains(type) ? DeliveryChannel.Unreliable : DeliveryChannel.ReliableOrdered;
            Assert.Equal(expected, MessageChannels.For(type));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => MessageChannels.For((MessageType)99));
        Assert.Equal(262144, MessageChannels.MaxFrameBytes(DeliveryChannel.ReliableOrdered));
        Assert.Equal(1150, MessageChannels.MaxFrameBytes(DeliveryChannel.Unreliable));
    }

    [Fact]
    public void Messages_EachRecordReturnsItsType_AndEveryTypeHasARecord()
    {
        var header = new ConstructHeaderRecord(new EntityId(1), FrameRef.Of(new PlanetId(1)), 0, ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 1);
        var frame = new SnapshotFrame(FrameRef.Of(new PlanetId(1)), 0);
        var state = new EntityState(new EntityId(1), EntityStateFields.None, 0, 0, Vector3.Zero, Quaternion.Identity, Vector3.Zero, 0);
        Message[] messages =
        [
            new ClientHello(1, 2, new PlatformIdentity(IdentityPlatform.Dev, 3), "n", "", []),
            new ServerHello(new WorldId(Guid.Empty), default, 60, new PlayerId(1), new EntityId(1), [], [], []),
            new HandshakeReject(HandshakeRejectCode.WrongProtocol, "update"),
            new ClientReady(),
            new TerrainCacheReport(new PlanetId(1), []),
            new TerrainBlockData(new TerrainBlockKey(new PlanetId(1), 0, 0, 0, 0), 1, []),
            new TerrainChecksums(new PlanetId(1), []),
            new ConstructState(header, 0, 1, []),
            new ConstructRemoved(new EntityId(1)),
            new OperationApplied(new OpId(1), default, new BlockClearOp(new EntityId(1), default), []),
            new IntentSubmit(1, new RemoveBlockIntent(new EntityId(1), default)),
            new IntentReply(1, RejectReason.None, new OpId(1)),
            new InputBatch(1, [new PlayerInput(1, 0, 0, 0, 0, InputButtons.Jump)]),
            new Snapshot(1, default, 0, 0, [frame], [state]),
            new SnapshotAck(1),
            new EntitySpawn(new EntityId(1), EntityKind.Player, 0, new PlayerId(1), frame, state, []),
            new EntityDespawn(new EntityId(1)),
            new Ping(1),
            new Pong(1, default),
            new Disconnect(DisconnectCode.Normal),
        ];

        foreach (var message in messages)
        {
            Assert.Equal(Enum.Parse<MessageType>(message.GetType().Name), message.Type);
        }

        var recordNames = typeof(Message).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Message)) && !t.IsAbstract)
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal);
        Assert.Equal(Enum.GetNames<MessageType>().Order(StringComparer.Ordinal), recordNames);
        Assert.Equal(Enum.GetValues<MessageType>().Order(), messages.Select(m => m.Type).Order());
    }

    [Fact]
    public void Operations_EachRecordReturnsItsKind_AndEveryKindHasARecord()
    {
        var names = typeof(Operation).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Operation)) && !t.IsAbstract)
            .Select(t => t.Name[..^"Op".Length])
            .Order(StringComparer.Ordinal);

        Assert.Equal(Enum.GetNames<OperationKind>().Order(StringComparer.Ordinal), names);
        Assert.Equal(OperationKind.BlockSet, new BlockSetOp(new EntityId(1), default, default).Kind);
        Assert.Equal(OperationKind.EntityReparent,
            new EntityReparentOp(new EntityId(1), default, 1, default, Quaternion.Identity, default, null).Kind);
    }
}
