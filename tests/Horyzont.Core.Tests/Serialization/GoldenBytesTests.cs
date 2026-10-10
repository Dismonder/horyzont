using System.Numerics;

using FsCheck.Fluent;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Hand-computed byte sequences. Each test checks both directions: the encoder produces exactly these bytes and the decoder returns the value.
/// The byte-by-byte derivation is in the comment above each constant.
/// </summary>
public sealed class GoldenBytesTests
{
    // BlockSet = 3, version 1, varuint 5 (construct), zigzag(1) = 2, zigzag(-1) = 1, zigzag(0) = 0, block 0x00030001 little-endian.
    private const string BlockSetHex = "03 01  05  02 01 00  01 00 03 00";

    // Ping = 18 (0x12), version 1, ClientTimeMicros 1 000 000 = 0x000F4240 as a fixed 8-byte little-endian ulong.
    private const string PingHex = "12 01  40 42 0F 00 00 00 00 00";

    // TerrainEdit = 1, version 1, Planet varuint 7, Shape Sphere = 1,
    // Center (1.0, -2.0, 0.5) = 3F800000 C0000000 3F000000 little-endian, Radius 2.5 = 40200000, Mode Add = 2, MaterialId 3.
    private const string TerrainEditHex = "01 01  07  01  00 00 80 3F  00 00 00 C0  00 00 00 3F  00 00 20 40  02  03";

    // ClientHello = 1, version 1, ProtocolVersion 1 (u16), BuildHash 0x0102030405060708 (u64), Identity: Steam = 2 + Value 0x11223344 (u64),
    // DisplayName "Ann", Password "pw", AuthTicket AA BB.
    private const string ClientHelloHex =
        "01 01  01 00  08 07 06 05 04 03 02 01  02  44 33 22 11 00 00 00 00  03 41 6E 6E  02 70 77  02 AA BB";

    // HandshakeReject = 3, version 1, Code WrongBuild = 2, Detail "old".
    private const string HandshakeRejectHex = "03 01  02  03 6F 6C 64";

    // Snapshot = 14 (0x0E), version 1, SnapshotId 300 = AC 02, ServerTick 1000 = E8 07, Baseline 299 = AB 02, LastProcessedInputSeq 5,
    // Frames: count 1, Planet = 2, Id 9, ParentSeq 0. Entities: count 1, Entity 42 (0x2A), Fields Position|Velocity = 5, FrameIndex 0, ParentSeq 3,
    // Position (1, 2, 3), Velocity as halves: 1.0 = 3C00, -2.0 = C000, 0 = 0000 (little-endian).
    private const string SnapshotHex =
        "0E 01  AC 02  E8 07  AB 02  05  01  02 09 00  01  2A 05 00 03  00 00 80 3F  00 00 00 40  00 00 40 40  00 3C  00 C0  00 00";

    // ConstructState = 8, version 1. Header: Id 5, Parent Planet(2) id 9, ParentSeq 0, Mode Static 0, Origin (0,0,0) = 12 zero bytes,
    // Rotation identity = X, Y, Z zero floats and W = 1.0 (00 00 80 3F), Version 1. PartIndex 0, PartCount 1. Chunks: count 1,
    // ChunkPos (0, -1, 0) = 00 01 00, RLE: 2 runs; run 1 = length 1, block 0x00000001; run 2 = length 4095 (FF 1F), block 0.
    private const string ConstructStateHex =
        "08 01  05  02 09  00  00  00 00 00 00 00 00 00 00 00 00 00 00  00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F  01"
        + "  00  01  01  00 01 00  02  01 01 00 00 00  FF 1F 00 00 00 00";

    // PlaceBlock code = 2, version 1, BlockTargetCode ConstructFace = 1, Construct 5, Cell (1, -1, 0) = 02 01 00, Face PosY = 2,
    // BlockTypeId 10, Orientation 3.
    private const string PlaceBlockHex = "02 01  01  05  02 01 00  02  0A  03";

    // RemoveBlock code = 3, version 1, Construct 5, Cell (0, 0, 0).
    private const string RemoveBlockHex = "03 01  05  00 00 00";

    // Blob: ConstructChunk = 3, version 1, RLE: 1 run, length 4096 = 80 20, block 0.
    private const string EmptyChunkBlobHex = "03 01  01  80 20  00 00 00 00";

    [Fact]
    public void BlockSetOp_EncodesToPlanExample()
    {
        var op = new BlockSetOp(new EntityId(5), new CellPos(1, -1, 0), BlockState.Create(1, 3));

        Assert.Equal(Kit.Hex(BlockSetHex), Kit.Encode(op));
        Assert.Equal(Kit.Hex("03 01 05 02 01 00 01 00 03 00"), Kit.Encode(op));
    }

    [Fact]
    public void BlockSetOp_DecodesPlanExample() =>
        DeepAssert.Equal(new BlockSetOp(new EntityId(5), new CellPos(1, -1, 0), BlockState.Create(1, 3)), Kit.DecodeOperation(Kit.Hex(BlockSetHex)));

    [Fact]
    public void BlockSetOp_EmptyBlock_IsStillAValidFrame() =>
        // The codec checks types only; whether an empty block makes sense is a rule of the validator and the applier.
        DeepAssert.Equal(
            new BlockSetOp(new EntityId(5), new CellPos(1, -1, 0), BlockState.Empty),
            Kit.DecodeOperation(Kit.Hex("03 01 05 02 01 00 00 00 00 00")));

    [Fact]
    public void Ping_EncodesToPlanExample()
    {
        Assert.Equal(Kit.Hex(PingHex), Kit.Encode(new Ping(1_000_000)));
        Assert.Equal(Kit.Hex("12 01 40 42 0F 00 00 00 00 00"), Kit.Encode(new Ping(1_000_000)));
    }

    [Fact]
    public void Ping_DecodesPlanExample() => DeepAssert.Equal(new Ping(1_000_000), MessageCodec.Read(Kit.Hex(PingHex)));

    [Fact]
    public void TerrainEditOp_EncodesToHandComputedBytes()
    {
        var op = new TerrainEditOp(new PlanetId(7), TerrainShape.Sphere, new Vector3(1f, -2f, 0.5f), 2.5f, TerrainEditMode.Add, 3);

        Assert.Equal(Kit.Hex(TerrainEditHex), Kit.Encode(op));
    }

    [Fact]
    public void TerrainEditOp_DecodesHandComputedBytes() =>
        DeepAssert.Equal(
            new TerrainEditOp(new PlanetId(7), TerrainShape.Sphere, new Vector3(1f, -2f, 0.5f), 2.5f, TerrainEditMode.Add, 3),
            Kit.DecodeOperation(Kit.Hex(TerrainEditHex)));

    [Fact]
    public void ClientHello_EncodesToHandComputedBytes()
    {
        var hello = new ClientHello(1, 0x0102030405060708UL, new PlatformIdentity(IdentityPlatform.Steam, 0x11223344), "Ann", "pw", [0xAA, 0xBB]);

        Assert.Equal(Kit.Hex(ClientHelloHex), Kit.Encode(hello));
    }

    [Fact]
    public void ClientHello_DecodesHandComputedBytes() =>
        DeepAssert.Equal(
            new ClientHello(1, 0x0102030405060708UL, new PlatformIdentity(IdentityPlatform.Steam, 0x11223344), "Ann", "pw", [0xAA, 0xBB]),
            MessageCodec.Read(Kit.Hex(ClientHelloHex)));

    [Fact]
    public void HandshakeReject_EncodesToHandComputedBytes() =>
        Assert.Equal(Kit.Hex(HandshakeRejectHex), Kit.Encode(new HandshakeReject(HandshakeRejectCode.WrongBuild, "old")));

    [Fact]
    public void HandshakeReject_DecodesHandComputedBytes() =>
        DeepAssert.Equal(new HandshakeReject(HandshakeRejectCode.WrongBuild, "old"), MessageCodec.Read(Kit.Hex(HandshakeRejectHex)));

    // ServerStatus = 22 (0x16), version 1, ReadOnly true = 01, Reason 2, Detail "x": length 1, 78.
    private const string ServerStatusHex = "16 01  01  02  01 78";

    // JoinUnavailable = 23 (0x17), version 1, Reason 3, Detail "é" (UTF-8 C3 A9): length 2.
    private const string JoinUnavailableHex = "17 01  03  02 C3 A9";

    [Fact]
    public void ServerStatus_EncodesToHandComputedBytes() => Assert.Equal(Kit.Hex(ServerStatusHex), Kit.Encode(new ServerStatus(true, 2, "x")));

    [Fact]
    public void ServerStatus_DecodesHandComputedBytes() => DeepAssert.Equal(new ServerStatus(true, 2, "x"), MessageCodec.Read(Kit.Hex(ServerStatusHex)));

    [Fact]
    public void JoinUnavailable_EncodesToHandComputedBytes() => Assert.Equal(Kit.Hex(JoinUnavailableHex), Kit.Encode(new JoinUnavailable(3, "é")));

    [Fact]
    public void JoinUnavailable_DecodesHandComputedBytes() => DeepAssert.Equal(new JoinUnavailable(3, "é"), MessageCodec.Read(Kit.Hex(JoinUnavailableHex)));

    public static Snapshot GoldenSnapshot() =>
        new(
            300,
            new Tick(1000),
            299,
            5,
            [new SnapshotFrame(new FrameRef(FrameKind.Planet, 9), 0)],
            [
                new EntityState(
                    new EntityId(42),
                    EntityStateFields.Position | EntityStateFields.Velocity,
                    0,
                    3,
                    new Vector3(1, 2, 3),
                    Quaternion.Identity,
                    new Vector3(1, -2, 0),
                    0),
            ]);

    [Fact]
    public void Snapshot_WithOneEntity_EncodesToHandComputedBytes() => Assert.Equal(Kit.Hex(SnapshotHex), Kit.Encode(GoldenSnapshot()));

    [Fact]
    public void Snapshot_WithOneEntity_DecodesHandComputedBytes()
    {
        var decoded = MessageCodec.Read(Kit.Hex(SnapshotHex));

        // Absent fields (rotation, motion flags) decode to defaults; rotation is exactly the identity quaternion.
        DeepAssert.Equal(GoldenSnapshot(), decoded);
        var entity = Assert.Single(Assert.IsType<Snapshot>(decoded).Entities);
        Assert.Equal(Quaternion.Identity, entity.Rotation);
        Assert.Equal(0, entity.MotionFlags);
    }

    private static ConstructState GoldenConstructState()
    {
        var blocks = new uint[4096];
        blocks[0] = BlockState.Create(1, 0).Packed;
        var id = new EntityId(5);
        return new ConstructState(
            new ConstructHeaderRecord(id, new FrameRef(FrameKind.Planet, 9), 0, ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 1),
            0,
            1,
            [new ConstructChunkRecord(new ConstructChunkKey(id, new ChunkPos(0, -1, 0)), blocks)]);
    }

    [Fact]
    public void ConstructState_WithOneChunk_EncodesToHandComputedBytes() =>
        Assert.Equal(Kit.Hex(ConstructStateHex), Kit.Encode(GoldenConstructState()));

    [Fact]
    public void ConstructState_WithOneChunk_DecodesHandComputedBytes()
    {
        var decoded = Assert.IsType<ConstructState>(MessageCodec.Read(Kit.Hex(ConstructStateHex)));

        DeepAssert.Equal(GoldenConstructState(), decoded);
        Assert.Equal(new EntityId(5), Assert.Single(decoded.Chunks).Key.Construct);
    }

    [Fact]
    public void PlaceBlockIntent_EncodesAndDecodesHandComputedBytes()
    {
        var intent = new PlaceBlockIntent(new ConstructFaceTarget(new EntityId(5), new CellPos(1, -1, 0), Face.PosY), 10, 3);

        Assert.Equal(Kit.Hex(PlaceBlockHex), Kit.Encode(intent));
        DeepAssert.Equal(intent, Kit.DecodeIntent(Kit.Hex(PlaceBlockHex)));
    }

    [Fact]
    public void RemoveBlockIntent_EncodesAndDecodesHandComputedBytes()
    {
        var intent = new RemoveBlockIntent(new EntityId(5), new CellPos(0, 0, 0));

        Assert.Equal(Kit.Hex(RemoveBlockHex), Kit.Encode(intent));
        DeepAssert.Equal(intent, Kit.DecodeIntent(Kit.Hex(RemoveBlockHex)));
    }

    [Fact]
    public void IntentSubmit_WrapsTheIntentFrameAfterClientSeq()
    {
        var message = new IntentSubmit(7, new RemoveBlockIntent(new EntityId(5), new CellPos(0, 0, 0)));

        // IntentSubmit = 11 (0x0B), version 1, ClientSeq 7, then the intent frame.
        Assert.Equal(Kit.Hex("0B 01 07 " + RemoveBlockHex), Kit.Encode(message));
        DeepAssert.Equal(message, MessageCodec.Read(Kit.Hex("0B 01 07 " + RemoveBlockHex)));
    }

    [Fact]
    public void OperationApplied_WrapsTheOperationFrameAndSortedChanges()
    {
        var message = new OperationApplied(
            new OpId(1),
            new Tick(1),
            new BlockClearOp(new EntityId(5), new CellPos(0, 0, 0)),
            [new ConstructChange(new EntityId(5), 1, false), new ConstructChange(new EntityId(6), 1, true)]);

        // OperationApplied = 10 (0x0A), version 1, Id 1, Tick 1, BlockClear frame (4, 1, construct 5, cell 0 0 0),
        // changes: count 2, (5, version 1, removed false), (6, version 1, removed true).
        var hex = "0A 01  01  01  04 01 05 00 00 00  02  05 01 00  06 01 01";
        Assert.Equal(Kit.Hex(hex), Kit.Encode(message));
        DeepAssert.Equal(message, MessageCodec.Read(Kit.Hex(hex)));
    }

    [Fact]
    public void Disconnect_EncodesAndDecodesHandComputedBytes()
    {
        // Disconnect = 20 (0x14), version 1, Code HostLeft = 5.
        Assert.Equal(Kit.Hex("14 01 05"), Kit.Encode(new Disconnect(DisconnectCode.HostLeft)));
        DeepAssert.Equal(new Disconnect(DisconnectCode.HostLeft), MessageCodec.Read(Kit.Hex("14 01 05")));
    }

    [Fact]
    public void ClientReady_IsTwoBytes()
    {
        Assert.Equal(Kit.Hex("04 01"), Kit.Encode(new ClientReady()));
        Assert.IsType<ClientReady>(MessageCodec.Read(Kit.Hex("04 01")));
    }

    [Fact]
    public void EmptyChunkBlob_HasKindVersionAndOneFullRun()
    {
        Assert.Equal(Kit.Hex(EmptyChunkBlobHex), StateCodec.EncodeChunkBlob(new uint[4096]));
        Assert.Equal(new uint[4096], StateCodec.DecodeChunkBlob(Kit.Hex(EmptyChunkBlobHex)));
    }

    [Fact]
    public void ChunkBlocks_AreRunLengthEncodedWithMaximalRuns()
    {
        var blocks = new uint[4096];
        blocks[0] = BlockState.Create(1, 0).Packed;
        blocks[1] = BlockState.Create(1, 0).Packed;
        blocks[4095] = BlockState.Create(2, 0).Packed;

        // 3 runs: (2 x 0x00000001), (4093 x 0 = FD 1F), (1 x 0x00000002).
        var expected = Kit.Hex("03  02 01 00 00 00  FD 1F 00 00 00 00  01 02 00 00 00");
        Assert.Equal(expected, Kit.Write(w => StateCodec.WriteChunkBlocks(w, blocks)));
        Assert.Equal(blocks, Kit.ReadAll(expected, (ref ByteReader r) => StateCodec.ReadChunkBlocks(ref r)));
    }

    [Fact]
    public void Blobs_StartWithTheirKindAndFormatVersionOne()
    {
        var header = new ConstructHeaderRecord(new EntityId(1), new FrameRef(FrameKind.System, 1), 0, ConstructMode.Ship, Vector3.Zero, Quaternion.Identity, 0);
        var player = new PlayerRecord(
            new PlayerId(1),
            new PlatformIdentity(IdentityPlatform.Dev, 1),
            "p",
            new EntityId(2),
            new FrameRef(FrameKind.System, 1),
            0,
            null,
            Vector3.Zero,
            Quaternion.Identity,
            0f,
            true);
        var planet = new Horyzont.Core.World.PlanetParams(new PlanetId(1), new SystemId(1), 7, "core:test/1");

        Assert.Equal(new byte[] { 1, 1 }, StateCodec.EncodeConstructHeaderBlob(header).AsSpan(0, 2).ToArray());
        Assert.Equal(new byte[] { 2, 1 }, StateCodec.EncodePlayerBlob(player).AsSpan(0, 2).ToArray());
        Assert.Equal(new byte[] { 3, 1 }, StateCodec.EncodeChunkBlob(new uint[4096]).AsSpan(0, 2).ToArray());
        Assert.Equal(new byte[] { 4, 1 }, StateCodec.EncodePlanetBlob(planet).AsSpan(0, 2).ToArray());
    }

    [Fact]
    public void MessageFrames_StartWithTheirTypeAndFormatVersionOne()
    {
        Assert.Equal(new byte[] { 13, 1 }, Kit.Encode(new InputBatch(0, [new PlayerInput(0, 0, 0, 0, 0, InputButtons.None)])).AsSpan(0, 2).ToArray());
        Assert.Equal(new byte[] { 15, 1 }, Kit.Encode(new SnapshotAck(1)).AsSpan(0, 2).ToArray());
        Assert.Equal(new byte[] { 17, 1 }, Kit.Encode(new EntityDespawn(new EntityId(1))).AsSpan(0, 2).ToArray());
        Assert.Equal(new byte[] { 9, 1 }, Kit.Encode(new ConstructRemoved(new EntityId(1))).AsSpan(0, 2).ToArray());
        Assert.Equal(new byte[] { 19, 1 }, Kit.Encode(new Pong(1, new Tick(1))).AsSpan(0, 2).ToArray());
    }

    [Theory]
    [InlineData(OperationKind.TerrainEdit)]
    [InlineData(OperationKind.ConstructCreate)]
    [InlineData(OperationKind.BlockSet)]
    [InlineData(OperationKind.BlockClear)]
    [InlineData(OperationKind.PlayerJoin)]
    [InlineData(OperationKind.PlayerLeave)]
    [InlineData(OperationKind.EntityReparent)]
    public void OperationFrames_StartWithOperationKindAndVersionOne(OperationKind kind)
    {
        var sample = CodecGen.Operation.Sample(200, 20).First(o => o.Kind == kind);

        Assert.Equal(new[] { (byte)kind, (byte)1 }, Kit.Encode(sample).AsSpan(0, 2).ToArray());
    }
}
