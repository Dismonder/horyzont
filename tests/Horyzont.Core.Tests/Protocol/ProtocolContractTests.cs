using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
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
            (OperationKind.PlayerJoin, 5), (OperationKind.PlayerLeave, 6), (OperationKind.EntityReparent, 7),
            (OperationKind.ShipModeSet, 20), (OperationKind.ShipFuelAdd, 21), (OperationKind.MarkerSet, 22), (OperationKind.MarkerRemove, 23),
            (OperationKind.Compound, 30), (OperationKind.ItemTransfer, 31), (OperationKind.ItemSwap, 32), (OperationKind.ItemCreate, 33),
            (OperationKind.ItemDestroy, 34), (OperationKind.ContainerCreate, 35),
            (OperationKind.ContainerRemove, 36), (OperationKind.ContainerConfigure, 37), (OperationKind.BlockEntitySet, 40), (OperationKind.ItemDiscover, 43), (OperationKind.RespawnPointSet, 44), (OperationKind.ProgressKeyGrant, 45),
            (OperationKind.BlockFunctionStateSet, 49));
        AssertValues((RejectReason.None, 0), (RejectReason.UnknownPlayer, 1), (RejectReason.PlayerOffline, 2), (RejectReason.RateLimited, 3),
            (RejectReason.InvalidParameters, 4), (RejectReason.RayOriginMismatch, 5), (RejectReason.NoHit, 6), (RejectReason.OutOfReach, 7),
            (RejectReason.AreaNotLoaded, 8), (RejectReason.BelowCore, 9), (RejectReason.OutsideAtmosphere, 10), (RejectReason.UnknownPlanet, 11),
            (RejectReason.UnknownConstruct, 12), (RejectReason.CellOutOfBounds, 13), (RejectReason.CellOccupied, 14), (RejectReason.CellEmpty, 15),
            (RejectReason.UnknownBlockType, 16), (RejectReason.UnknownMaterial, 17), (RejectReason.IntersectsPlayer, 18),
            (RejectReason.NoLineOfSight, 19), (RejectReason.WrongPlanet, 20), (RejectReason.ServerReadOnly, 21),
            (RejectReason.NoCockpit, 30), (RejectReason.ShipAnchored, 31), (RejectReason.NoEngine, 37), (RejectReason.MarkerLimit, 36), (RejectReason.ShipNotParked, 39),
            (RejectReason.InventoryFull, 40), (RejectReason.NotEnoughItems, 41), (RejectReason.ContainerChanged, 42), (RejectReason.ContainerLocked, 43),
            (RejectReason.NotOwner, 54), (RejectReason.ContainerNotEmpty, 57));
        AssertValues((MessageType.ClientHello, 1), (MessageType.ServerHello, 2), (MessageType.HandshakeReject, 3), (MessageType.ClientReady, 4),
            (MessageType.TerrainCacheReport, 5), (MessageType.TerrainBlockData, 6), (MessageType.TerrainChecksums, 7), (MessageType.ConstructState, 8),
            (MessageType.ConstructRemoved, 9), (MessageType.OperationApplied, 10), (MessageType.IntentSubmit, 11), (MessageType.IntentReply, 12),
            (MessageType.InputBatch, 13), (MessageType.Snapshot, 14), (MessageType.SnapshotAck, 15), (MessageType.EntitySpawn, 16),
            (MessageType.EntityDespawn, 17), (MessageType.Ping, 18), (MessageType.Pong, 19), (MessageType.Disconnect, 20), (MessageType.ResyncRequest, 21), (MessageType.ServerStatus, 22), (MessageType.JoinUnavailable, 23), (MessageType.TerrainSync, 24), (MessageType.TerrainView, 25),
            (MessageType.ShipStatus, 31), (MessageType.StarSystemState, 32),
            (MessageType.MarkersState, 33),
            (MessageType.ContainerState, 40), (MessageType.ContainerDelta, 41), (MessageType.ContainerSubscribe, 42),
            (MessageType.ContainerUnsubscribe, 43), (MessageType.BlockEntityStates, 46), (MessageType.ItemRegistry, 49));
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
            (BlobKind.TerrainBlock, 5), (BlobKind.StarSystemParams, 10), (BlobKind.Marker, 11), (BlobKind.ShipState, 12), (BlobKind.Container, 20),
            (BlobKind.BlockEntity, 21), (BlobKind.PlayerProgress, 22), (BlobKind.ProgressKeys, 25));
        AssertValues((ItemSource.Harvest, 1), (ItemSource.Craft, 2), (ItemSource.Station, 3), (ItemSource.Crop, 4), (ItemSource.Refund, 5),
            (ItemSource.Loot, 6), (ItemSource.Admin, 7), (ItemSource.Recovered, 8), (ItemSource.Starter, 9));
        AssertValues((ItemSink.Craft, 1), (ItemSink.Station, 2), (ItemSink.Eat, 3), (ItemSink.Fuel, 4), (ItemSink.BuildCost, 5),
            (ItemSink.SuitRefill, 6), (ItemSink.Plant, 7), (ItemSink.Despawn, 8), (ItemSink.Admin, 9), (ItemSink.Ammo, 10), (ItemSink.Offering, 11));
        AssertValues((ContainerKind.PlayerBag, 1), (ContainerKind.Hotbar, 2), (ContainerKind.Equipment, 3), (ContainerKind.Chest, 4),
            (ContainerKind.CargoHold, 5), (ContainerKind.StationInput, 6), (ContainerKind.StationOutput, 7), (ContainerKind.Tombstone, 8),
            (ContainerKind.DroppedItem, 9));
        AssertValues((ContainerHostKind.Player, 1), (ContainerHostKind.ConstructBlock, 2), (ContainerHostKind.PersistentEntity, 3),
            (ContainerHostKind.Poi, 4));
        AssertValues((ContainerAccess.Shared, 1), (ContainerAccess.Private, 2));
        AssertValues((BlockEntityKind.Chest, 1), (BlockEntityKind.Bed, 2), (BlockEntityKind.Station, 3), (BlockEntityKind.Teleport, 4),
            (BlockEntityKind.OxygenGenerator, 5), (BlockEntityKind.Planter, 6), (BlockEntityKind.CargoHold, 7));
        AssertValues((BlockInteractAction.Toggle, 1), (BlockInteractAction.Sleep, 2), (BlockInteractAction.SetRespawn, 3));
        AssertValues((BlockConfigKind.TeleportName, 1), (BlockConfigKind.ContainerAccess, 2));
        AssertValues((TerrainShape.Sphere, 1));
        AssertValues((TerrainEditMode.Remove, 1), (TerrainEditMode.Add, 2));
        AssertValues((EntityKind.Player, 1), (EntityKind.Construct, 2));
        AssertValues((EntityStateFields.None, 0), (EntityStateFields.Position, 1), (EntityStateFields.Rotation, 2), (EntityStateFields.Velocity, 4),
            (EntityStateFields.MotionFlags, 8));
        AssertValues((InputButtons.None, 0), (InputButtons.Jump, 1), (InputButtons.Sprint, 2), (InputButtons.Crouch, 4), (InputButtons.Primary, 8),
            (InputButtons.Secondary, 16), (InputButtons.DevFly, 32));
    }

    /// <summary>
    /// K2-10 adds reject reason 21 (and raised the protocol version from 1 to 2; K2-11 raises it to 3, K6-29W to 14, K6-30 to 15, see <see cref="ProtocolVersion_MatchesDocumentedMergeValue"/>).
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

    /// <summary>The Space reject reasons of K5-23 and K5-24 round-trip; the gaps of the 30-39 range stay undefined until their packages.</summary>
    [Theory]
    [InlineData(RejectReason.NoCockpit, 30)]
    [InlineData(RejectReason.ShipAnchored, 31)]
    [InlineData(RejectReason.NoEngine, 37)]
    [InlineData(RejectReason.ShipNotParked, 39)]
    [InlineData(RejectReason.MarkerLimit, 36)]
    public void RejectReason_KosmosValues_Golden(RejectReason reason, byte value)
    {
        var reply = new IntentReply(7, reason, OpId.None);
        var writer = new ByteWriter();

        MessageCodec.Write(writer, reply);
        var bytes = writer.WrittenSpan.ToArray();

        Assert.Equal(new byte[] { 12, 1, 7, value, 0 }, bytes);
        Assert.Equal(reply, MessageCodec.Read(bytes));
        foreach (byte undefined in new byte[] { 22, 29, 32, 33, 34, 35, 38, 44, 255 })
        {
            var copy = (byte[])bytes.Clone();
            copy[3] = undefined;
            Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(copy));
        }
    }

    /// <summary>
    /// K3: every package that changes Protocol/ or Serialization/ sets main + 1 and updates this test and Constants_GoldenValues.
    /// Gate decision D4: the value on main plus one. K2-10 raised it from 1 to 2; K2-11 adds <see cref="InputButtons.DevFly"/> and
    /// the 0x3F button mask and raises it to 3. K5-20 raises it for ship-mode constructs in snapshots (semantics only, main + 1).
    /// K5-21 adds StarSystemState (32) on the K5-20 stack: K5-20 + 1, recomputed as main + 1 when K5-21 merges.
    /// K5-22 changes the acceptance rule of TerrainView (another planet up to 4.4 R) on the K5-21 stack: K5-21 + 1 (P1, D4).
    /// K5-24 adds ShipFuelAddOp (21), RefuelIntent (13) and ShipNotParked (39) after K5-22 (main 10): 11.
    /// K5-27 adds MarkersState (33), operations 22/23, intents 14/15, reject reason 36 and blob 11 after K5-24 (main 11): 12.
    /// K5-26 adds ShipStatus (31) and the seat in a player's EntitySpawn.KindData after K5-27 (main 12): 13.
    /// K6-29W adds items (intents 20-21, reject reasons 40-43, messages 40-43 and 49) after K5-26 (main 13): 14.
    /// K6-30 adds operations 36, 37, 40, 44, 49, intents 22, 23, 28, 29, reject reasons 54 and 57, message 46 and blobs 21, 22 after K6-29W (main 14): 15.
    /// A rebase onto a newer main updates this value, the constant and the package plan together.
    /// </summary>
    [Fact]
    public void ProtocolVersion_MatchesDocumentedMergeValue()
    {
        Assert.Equal(15, ProtocolConstants.ProtocolVersion);
        Assert.Equal(0x3F, ProtocolConstants.InputButtonsMask);
    }

    [Fact]
    public void Constants_GoldenValues()
    {
        Assert.Equal(15, ProtocolConstants.ProtocolVersion);
        Assert.Equal(262144, ProtocolConstants.MaxReliableMessageBytes);
        Assert.Equal(1150, ProtocolConstants.MaxUnreliableMessageBytes);
        Assert.Equal(32, ProtocolConstants.MaxDisplayNameBytes);
        Assert.Equal(64, ProtocolConstants.MaxRegistryKeyBytes);
        Assert.Equal(2048, ProtocolConstants.MaxBlockTypes);
        Assert.Equal(255, ProtocolConstants.MaxMaterials);
        Assert.Equal(22, ProtocolConstants.MaxEntitiesPerSnapshot);
        Assert.Equal(8, ProtocolConstants.MaxFramesPerSnapshot);
        Assert.Equal(4096, ProtocolConstants.MaxItemTypes);
        Assert.Equal(256, ProtocolConstants.MaxContainerSlots);
        Assert.Equal(4096, ProtocolConstants.MaxCompoundComponents);
        Assert.Equal(160 * 1024, ProtocolConstants.MaxCompoundBytes);
        Assert.Equal(4096, ProtocolConstants.MaxBlockEntitiesPerConstruct);
        Assert.Equal(32, ProtocolConstants.MaxTeleportNameBytes);
    }

    [Fact]
    public void MessageChannels_MapsEveryType()
    {
        var unreliable = new[] { MessageType.InputBatch, MessageType.Snapshot, MessageType.SnapshotAck, MessageType.Ping, MessageType.Pong, MessageType.ShipStatus };
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
            new ResyncRequest(new EntityId(1)),
            new TerrainSync(1, 0, new PlanetId(1), default, 0, true, []),
            new TerrainView(new PlanetId(1), 1, Vector3.Zero, 2048, Vector3.Zero, 64),
            new ServerStatus(true, 2, "Writer unavailable"),
            new JoinUnavailable(1, "Terrain mismatch"),
            new StarSystemState(Horyzont.Core.Space.StarSystemGenesis.CreateWorldBodies(1, "core:planet/1").System),
            new ShipStatus(new EntityId(1), default, 1f, 2f, 3f, default, default),
            new MarkersState([]),
            ContainerState.Rejected(new ContainerId(1), RejectReason.ContainerLocked),
            new ContainerDelta(new ContainerId(1), 1, []),
            new ContainerSubscribe(new ContainerId(1)),
            new ContainerUnsubscribe(new ContainerId(1)),
            new BlockEntityStates(new EntityId(1), []),
            new ItemRegistry([]),
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
