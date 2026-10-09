using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Malformed-input cases for message frames, built from hand-written hex. Every "control" test decodes a nearly identical valid frame,
/// so a rejection can only come from the single value that was changed.
/// </summary>
public sealed class MessageEdgeCaseTests
{
    private const string Zero12 = "00 00 00 00 00 00 00 00 00 00 00 00";

    // One PlayerInput: ClientTick 0, MoveX 0, MoveY 0, Yaw 0.0, Pitch 0.0, Buttons 0 (13 bytes).
    private const string ZeroInput = "00  00  00  00 00 00 00  00 00 00 00  00 00";

    private static string Repeat(string text, int count) => string.Join(' ', Enumerable.Repeat(text, count));

    // ----- unknown type / version / trailing bytes ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("00 01")]
    [InlineData("15 01")]
    [InlineData("FF 01")]
    public void Read_UnknownMessageType_ThrowsProtocolViolation(string hex) => Kit.AssertMessageViolation(hex);

    [Theory]
    [InlineData("04 00")]
    [InlineData("04 02")]
    [InlineData("04 FF")]
    [InlineData("12 00 40 42 0F 00 00 00 00 00")]
    [InlineData("12 02 40 42 0F 00 00 00 00 00")]
    [InlineData("0F 02 01")]
    public void Read_UnknownFormatVersion_ThrowsProtocolViolation(string hex) => Kit.AssertMessageViolation(hex);

    [Theory]
    [InlineData("")]
    [InlineData("04")]
    [InlineData("12")]
    public void Read_FrameShorterThanHeader_ThrowsProtocolViolation(string hex) => Kit.AssertMessageViolation(hex);

    [Theory]
    [InlineData("04 01 00")]
    [InlineData("12 01 40 42 0F 00 00 00 00 00 00")]
    [InlineData("0F 01 01 01")]
    [InlineData("14 01 00 00")]
    public void Read_TrailingBytesAfterPayload_ThrowsProtocolViolation(string hex) => Kit.AssertMessageViolation(hex);

    // ----- varuint / varint range checks --------------------------------------------------------------------------------------------------

    [Fact]
    public void Read_SnapshotAck_ControlDecodes() => DeepAssert.Equal(new SnapshotAck(300), MessageCodec.Read(Kit.Hex("0F 01 AC 02")));

    [Theory]
    [InlineData("0F 01 80 00")]
    [InlineData("0F 01 AC 82 00")]
    [InlineData("0F 01 FF FF FF FF FF FF FF FF FF 02")]
    [InlineData("0F 01 80 80 80 80 10")]
    [InlineData("0F 01 FF FF FF FF 1F")]
    [InlineData("0F 01 80")]
    public void Read_SnapshotAckWithBadVarUInt_ThrowsProtocolViolation(string hex) => Kit.AssertMessageViolation(hex);

    [Fact]
    public void Read_BlockSetOp_ControlDecodes() =>
        Assert.IsType<BlockSetOp>(Kit.DecodeOperation(Kit.Hex("03 01 05 02 01 00 01 00 03 00")));

    [Theory]
    [InlineData("03 01 05 80 80 80 80 10 01 00 01 00 03 00")]
    [InlineData("03 01 05 02 81 80 80 80 10 00 01 00 03 00")]
    [InlineData("03 01 05 02 01 FF FF FF FF 1F 01 00 03 00")]
    [InlineData("03 01 05 02 80 00 00 01 00 03 00")]
    public void Read_VarIntBeyondIntRangeOrRedundant_ThrowsProtocolViolation(string hex) => Kit.AssertOperationViolation(hex);

    // ----- BlockState ----------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("01 00 03 10")]
    [InlineData("01 00 03 80")]
    [InlineData("01 00 03 F0")]
    [InlineData("00 00 03 00")]
    [InlineData("01 00 18 00")]
    [InlineData("01 00 1F 00")]
    public void Read_MalformedBlockState_ThrowsProtocolViolation(string block) =>
        Kit.AssertOperationViolation("03 01 05 02 01 00 " + block);

    [Theory]
    [InlineData("01 00 03 0E")]
    [InlineData("01 00 E3 01")]
    [InlineData("FF FF 17 0F")]
    public void Read_BlockStateWithAnyFunctionStateAndCostRevision_IsAccepted(string block)
    {
        var op = Assert.IsType<BlockSetOp>(Kit.DecodeOperation(Kit.Hex("03 01 05 02 01 00 " + block)));

        Assert.True(op.Block.IsWellFormed);
    }

    // ----- sbyte, flags, enums ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Read_InputBatch_ControlDecodes()
    {
        var batch = Assert.IsType<InputBatch>(MessageCodec.Read(Kit.Hex("0D 01 00 01 " + ZeroInput)));

        var input = Assert.Single(batch.Inputs);
        Assert.Equal(0f, input.MoveX);
        Assert.Equal(InputButtons.None, input.Buttons);
    }

    [Theory]
    [InlineData("00  80  00")]
    [InlineData("00  00  80")]
    public void Read_InputBatchWithMinusOneTwentyEight_ThrowsProtocolViolation(string tickAndAxes) =>
        Kit.AssertMessageViolation("0D 01 00 01 " + tickAndAxes + "  00 00 00 00  00 00 00 00  00 00");

    [Theory]
    [InlineData("3F 00", InputButtons.Jump | InputButtons.Sprint | InputButtons.Crouch | InputButtons.Primary | InputButtons.Secondary | InputButtons.DevFly)]
    [InlineData("1F 00", InputButtons.Jump | InputButtons.Sprint | InputButtons.Crouch | InputButtons.Primary | InputButtons.Secondary)]
    [InlineData("20 00", InputButtons.DevFly)]
    [InlineData("01 00", InputButtons.Jump)]
    public void Read_InputButtonsInsideMask_AreAccepted(string buttons, InputButtons expected)
    {
        var batch = Assert.IsType<InputBatch>(MessageCodec.Read(Kit.Hex("0D 01 00 01 00 00 00 00 00 00 00 00 00 00 00 " + buttons)));

        Assert.Equal(expected, batch.Inputs[0].Buttons);
    }

    [Fact]
    public void InputBatch_DevFlyBit_RoundTrips()
    {
        var input = new PlayerInput(5, 0.5f, -1f, 1.25f, -0.5f, InputButtons.DevFly | InputButtons.Jump | InputButtons.Sprint);
        var bytes = Kit.Encode(new InputBatch(9, [input]));

        var batch = Assert.IsType<InputBatch>(MessageCodec.Read(bytes));

        Assert.Equal(InputButtons.DevFly | InputButtons.Jump | InputButtons.Sprint, batch.Inputs[0].Buttons);
        Assert.Equal(new byte[] { 0x23, 0x00 }, bytes[^2..]);
    }

    [Theory]
    [InlineData("40 00")]
    [InlineData("00 01")]
    [InlineData("FF FF")]
    [InlineData("80 00")]
    public void InputBatch_BitsAbove0x3F_ProtocolViolation(string buttons) =>
        Kit.AssertMessageViolation("0D 01 00 01 00 00 00 00 00 00 00 00 00 00 00 " + buttons);

    [Theory]
    [InlineData("00")]
    [InlineData("06")]
    [InlineData("FF")]
    public void Read_InputBatchInputCountOutOfRange_ThrowsProtocolViolation(string count)
    {
        // Enough data for six inputs, so a rejection is caused by the count and not by missing bytes.
        Kit.AssertMessageViolation("0D 01 00 " + count + " " + Repeat(ZeroInput, 6));
    }

    [Fact]
    public void Read_InputBatchWithFiveInputs_IsAccepted()
    {
        var batch = Assert.IsType<InputBatch>(MessageCodec.Read(Kit.Hex("0D 01 00 05 " + Repeat(ZeroInput, 5))));

        Assert.Equal(5, batch.Inputs.Count);
    }

    [Theory]
    [InlineData("07")]
    [InlineData("FF")]
    public void Read_DisconnectWithUnknownCode_ThrowsProtocolViolation(string code) => Kit.AssertMessageViolation("14 01 " + code);

    [Theory]
    [InlineData("00")]
    [InlineData("06")]
    public void Read_DisconnectWithKnownCode_IsAccepted(string code) =>
        Assert.IsType<Disconnect>(MessageCodec.Read(Kit.Hex("14 01 " + code)));

    [Theory]
    [InlineData("16")]
    [InlineData("FF")]
    public void Read_IntentReplyWithUnknownReason_ThrowsProtocolViolation(string reason) => Kit.AssertMessageViolation("0C 01 01 " + reason + " 00");

    [Theory]
    [InlineData("00")]
    [InlineData("14")]
    [InlineData("15")]
    public void Read_IntentReplyWithKnownReason_IsAccepted(string reason) =>
        Assert.IsType<IntentReply>(MessageCodec.Read(Kit.Hex("0C 01 01 " + reason + " 00")));

    // ----- ClientHello / HandshakeReject / strings ------------------------------------------------------------------------------------------------

    private static string Hello(string name = "03 41 6E 6E", string password = "02 70 77", string ticket = "02 AA BB", string platform = "02") =>
        "01 01  01 00  08 07 06 05 04 03 02 01  " + platform + "  44 33 22 11 00 00 00 00  " + name + "  " + password + "  " + ticket;

    [Fact]
    public void Read_ClientHello_ControlDecodes() => Assert.IsType<ClientHello>(MessageCodec.Read(Kit.Hex(Hello())));

    [Theory]
    [InlineData("00")]
    [InlineData("03")]
    [InlineData("FF")]
    public void Read_ClientHelloWithUnknownPlatform_ThrowsProtocolViolation(string platform) =>
        Kit.AssertMessageViolation(Hello(platform: platform));

    [Theory]
    [InlineData("02 C3 28")]
    [InlineData("01 FF")]
    [InlineData("03 ED A0 80")]
    public void Read_ClientHelloWithInvalidUtf8_ThrowsProtocolViolation(string name) => Kit.AssertMessageViolation(Hello(name: name));

    [Fact]
    public void Read_ClientHelloNameAtAndOverLimit()
    {
        var at = Assert.IsType<ClientHello>(MessageCodec.Read(Kit.Hex(Hello(name: "20 " + Repeat("41", 32)))));
        Assert.Equal(32, at.DisplayName.Length);

        Kit.AssertMessageViolation(Hello(name: "21 " + Repeat("41", 33)));
    }

    [Fact]
    public void Read_ClientHelloPasswordAtAndOverLimit()
    {
        var at = Assert.IsType<ClientHello>(MessageCodec.Read(Kit.Hex(Hello(password: "40 " + Repeat("41", 64)))));
        Assert.Equal(64, at.Password.Length);

        Kit.AssertMessageViolation(Hello(password: "41 " + Repeat("41", 65)));
    }

    [Fact]
    public void Read_ClientHelloTicketAtAndOverLimit()
    {
        var at = Assert.IsType<ClientHello>(MessageCodec.Read(Kit.Hex(Hello(ticket: "80 08 " + Repeat("41", 1024)))));
        Assert.Equal(1024, at.AuthTicket.Length);

        Kit.AssertMessageViolation(Hello(ticket: "81 08 " + Repeat("41", 1025)));
    }

    [Fact]
    public void Read_HandshakeRejectDetailAtAndOverLimit()
    {
        var at = Assert.IsType<HandshakeReject>(MessageCodec.Read(Kit.Hex("03 01 01 80 02 " + Repeat("41", 256))));
        Assert.Equal(256, at.Detail.Length);

        Kit.AssertMessageViolation("03 01 01 81 02 " + Repeat("41", 257));
    }

    [Fact]
    public void Read_HandshakeRejectWithInvalidUtf8Detail_ThrowsProtocolViolation() => Kit.AssertMessageViolation("03 01 01 02 C3 28");

    // ----- EntitySpawn ------------------------------------------------------------------------------------------------------------------------

    private static string Spawn(
        string kind = "01", string archetype = "01", string owner = "00", string frame = "02 09 00", string fields = "00", string frameIndex = "00",
        string data = "00") =>
        "10 01  05  " + kind + "  " + archetype + "  " + owner + "  " + frame + "  05 " + fields + " " + frameIndex + " 00  " + data;

    [Fact]
    public void Read_EntitySpawn_ControlDecodes()
    {
        var spawn = Assert.IsType<EntitySpawn>(MessageCodec.Read(Kit.Hex(Spawn())));

        Assert.Equal(new EntityId(5), spawn.Entity);
        Assert.Equal(EntityKind.Player, spawn.Kind);
        Assert.Equal((ushort)1, spawn.Archetype);
        Assert.Null(spawn.Owner);
        Assert.Equal(EntityStateFields.None, spawn.Initial.Fields);
        Assert.Equal(Quaternion.Identity, spawn.Initial.Rotation);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("03")]
    [InlineData("FF")]
    public void Read_EntitySpawnWithUnknownKind_ThrowsProtocolViolation(string kind) => Kit.AssertMessageViolation(Spawn(kind: kind));

    [Fact]
    public void Read_EntitySpawnArchetype_RangeIsUShort()
    {
        var max = Assert.IsType<EntitySpawn>(MessageCodec.Read(Kit.Hex(Spawn(archetype: "FF FF 03"))));
        Assert.Equal(ushort.MaxValue, max.Archetype);

        Kit.AssertMessageViolation(Spawn(archetype: "80 80 04"));
    }

    [Theory]
    [InlineData("02")]
    [InlineData("FF")]
    public void Read_EntitySpawnWithBadOptionalFlag_ThrowsProtocolViolation(string owner) => Kit.AssertMessageViolation(Spawn(owner: owner));

    [Fact]
    public void Read_EntitySpawnWithOwner_IsAccepted()
    {
        var spawn = Assert.IsType<EntitySpawn>(MessageCodec.Read(Kit.Hex(Spawn(owner: "01 07"))));

        Assert.Equal(new PlayerId(7), spawn.Owner);
    }

    [Theory]
    [InlineData("00 09 00")]
    [InlineData("04 09 00")]
    [InlineData("FF 09 00")]
    public void Read_EntitySpawnWithUnknownFrameKind_ThrowsProtocolViolation(string frame) => Kit.AssertMessageViolation(Spawn(frame: frame));

    [Theory]
    [InlineData("10")]
    [InlineData("20")]
    [InlineData("80")]
    [InlineData("1F")]
    public void Read_EntityStateFieldsOutsideMask_ThrowsProtocolViolation(string fields) => Kit.AssertMessageViolation(Spawn(fields: fields));

    [Fact]
    public void Read_EntitySpawnInitialFrameIndexMustBeZero() => Kit.AssertMessageViolation(Spawn(frameIndex: "01"));

    [Fact]
    public void Read_EntitySpawnKindDataAtAndOverLimit()
    {
        var at = Assert.IsType<EntitySpawn>(MessageCodec.Read(Kit.Hex(Spawn(data: "40 " + Repeat("41", 64)))));
        Assert.Equal(64, at.KindData.Length);

        Kit.AssertMessageViolation(Spawn(data: "41 " + Repeat("41", 65)));
    }

    [Fact]
    public void Read_EntitySpawnWithPositionAndMotionFlags_DecodesOnlyPresentFields()
    {
        // Fields = Position | MotionFlags = 9: position (1, 2, 3) then motion flags 0xFF; rotation and velocity stay default.
        var hex = "10 01  05  01  01  00  02 09 00  05 09 00 00  00 00 80 3F  00 00 00 40  00 00 40 40  FF  00";

        var spawn = Assert.IsType<EntitySpawn>(MessageCodec.Read(Kit.Hex(hex)));

        Assert.Equal(new Vector3(1, 2, 3), spawn.Initial.Position);
        Assert.Equal((byte)0xFF, spawn.Initial.MotionFlags);
        Assert.Equal(Vector3.Zero, spawn.Initial.Velocity);
        Assert.Equal(Quaternion.Identity, spawn.Initial.Rotation);
    }

    // ----- Snapshot -----------------------------------------------------------------------------------------------------------------------------

    private static string Snap(string frames = "01  02 09 00", string entities = "00") => "0E 01  00  00  00  00  " + frames + "  " + entities;

    private static string Frames(int count) =>
        count.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "  " + string.Join("  ", Enumerable.Range(1, count).Select(i => $"02 {i:X2} 00"));

    [Fact]
    public void Read_Snapshot_ControlDecodes()
    {
        var snapshot = Assert.IsType<Snapshot>(MessageCodec.Read(Kit.Hex(Snap())));

        Assert.Single(snapshot.Frames);
        Assert.Empty(snapshot.Entities);
    }

    [Fact]
    public void Read_SnapshotFrameCount_MustBeOneToEightDistinct()
    {
        Assert.Equal(8, Assert.IsType<Snapshot>(MessageCodec.Read(Kit.Hex(Snap(Frames(8))))).Frames.Count);
        Kit.AssertMessageViolation(Snap(Frames(0)));
        Kit.AssertMessageViolation(Snap(Frames(9)));
        Kit.AssertMessageViolation(Snap("02  02 09 00  02 09 00"));
    }

    [Fact]
    public void Read_SnapshotEntityCount_MustBeAtMostTwentyTwo()
    {
        var entity = "2A 00 00 00";

        Assert.Equal(22, Assert.IsType<Snapshot>(MessageCodec.Read(Kit.Hex(Snap(entities: "16 " + Repeat(entity, 22))))).Entities.Count);
        Kit.AssertMessageViolation(Snap(entities: "17 " + Repeat(entity, 23)));
    }

    [Fact]
    public void Read_SnapshotFrameIndexOutsideFrameTable_ThrowsProtocolViolation()
    {
        Assert.IsType<Snapshot>(MessageCodec.Read(Kit.Hex(Snap(Frames(2), "01  2A 00 01 00"))));

        Kit.AssertMessageViolation(Snap(Frames(2), "01  2A 00 02 00"));
        Kit.AssertMessageViolation(Snap(Frames(1), "01  2A 00 01 00"));
        Kit.AssertMessageViolation(Snap(Frames(1), "01  2A 00 FF 00"));
    }

    [Theory]
    [InlineData("10")]
    [InlineData("20")]
    [InlineData("FF")]
    public void Read_SnapshotEntityFieldsOutsideMask_ThrowsProtocolViolation(string fields) =>
        Kit.AssertMessageViolation(Snap(entities: "01  2A " + fields + " 00 00"));

    [Fact]
    public void Read_SnapshotEntityWithRotation_DecodesWithinTolerance()
    {
        // 0xE0080200 little-endian is the packed identity rotation.
        var snapshot = Assert.IsType<Snapshot>(MessageCodec.Read(Kit.Hex(Snap(entities: "01  2A 02 00 00  00 02 08 E0"))));

        Assert.True(Kit.AngleDegrees(Quaternion.Identity, snapshot.Entities[0].Rotation) < 0.3);
    }

    [Theory]
    [InlineData("FF FF FF 3F")]
    [InlineData("FF FF FF 7F")]
    [InlineData("FF FF FF FF")]
    public void Read_SnapshotEntityWithRotationOutsideUnitSphere_ThrowsProtocolViolation(string packed) =>
        Kit.AssertMessageViolation(Snap(entities: "01  2A 02 00 00  " + packed));

    [Theory]
    [InlineData("00 7E")]
    [InlineData("00 7C")]
    [InlineData("00 FC")]
    public void Read_SnapshotEntityWithNonFiniteVelocity_ThrowsProtocolViolation(string half) =>
        Kit.AssertMessageViolation(Snap(entities: "01  2A 04 00 00  00 00  00 00  " + half));

    [Fact]
    public void Read_SnapshotEntityWithVelocityAndMotion_DecodesFieldsInOrder()
    {
        // Fields = Velocity | MotionFlags = 12: three halves (1.0, 0, -2.0), then the motion flags byte.
        var snapshot = Assert.IsType<Snapshot>(MessageCodec.Read(Kit.Hex(Snap(entities: "01  2A 0C 00 00  00 3C  00 00  00 C0  07"))));

        var e = snapshot.Entities[0];
        Assert.Equal(new Vector3(1, 0, -2), e.Velocity);
        Assert.Equal((byte)7, e.MotionFlags);
        Assert.Equal(Vector3.Zero, e.Position);
    }

    // ----- ConstructState -----------------------------------------------------------------------------------------------------------------------

    private const string CsHeader = "05  02 09  00  00  " + Zero12 + "  " + Zero12 + "  00 00 80 3F  01";

    private static string Chunk(string pos, string blocks = "02  01 01 00 00 00  FF 1F 00 00 00 00") => pos + "  " + blocks;

    private static string State(string partIndex = "00", string partCount = "01", string chunks = "00", string header = CsHeader) =>
        "08 01  " + header + "  " + partIndex + "  " + partCount + "  " + chunks;

    [Fact]
    public void Read_ConstructState_ControlDecodes()
    {
        var state = Assert.IsType<ConstructState>(MessageCodec.Read(Kit.Hex(State())));

        Assert.Equal(new EntityId(5), state.Header.Id);
        Assert.Empty(state.Chunks);
    }

    [Theory]
    [InlineData("00", "00")]
    [InlineData("01", "01")]
    [InlineData("02", "01")]
    [InlineData("00", "80 80 04")]
    [InlineData("80 80 04", "FF FF 03")]
    public void Read_ConstructStateWithBadPartNumbers_ThrowsProtocolViolation(string index, string count) =>
        Kit.AssertMessageViolation(State(index, count));

    [Fact]
    public void Read_ConstructStateWithLargestPartNumbers_IsAccepted()
    {
        var state = Assert.IsType<ConstructState>(MessageCodec.Read(Kit.Hex(State("FE FF 03", "FF FF 03"))));

        Assert.Equal((ushort)65534, state.PartIndex);
        Assert.Equal(ushort.MaxValue, state.PartCount);
    }

    [Fact]
    public void Read_ConstructStateWithUnknownMode_ThrowsProtocolViolation()
    {
        var header = "05  02 09  00  02  " + Zero12 + "  " + Zero12 + "  00 00 80 3F  01";

        Kit.AssertMessageViolation(State(header: header));
    }

    [Fact]
    public void Read_ConstructStateChunks_GetTheHeaderConstructAsKey()
    {
        var state = Assert.IsType<ConstructState>(MessageCodec.Read(Kit.Hex(State(chunks: "02  " + Chunk("00 00 00") + "  " + Chunk("02 00 00")))));

        Assert.Equal(2, state.Chunks.Count);
        Assert.All(state.Chunks, c => Assert.Equal(new EntityId(5), c.Key.Construct));
        Assert.Equal(new[] { new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0) }, state.Chunks.Select(c => c.Key.Chunk));
    }

    [Fact]
    public void Read_ConstructStateWithRepeatedChunkPosition_ThrowsProtocolViolation() =>
        Kit.AssertMessageViolation(State(chunks: "02  " + Chunk("00 00 00") + "  " + Chunk("00 00 00")));

    [Fact]
    public void Read_ConstructStateChunkCount_MustBeAtMostTwelve()
    {
        static string Many(int count) => count.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "  "
            + string.Join("  ", Enumerable.Range(0, count).Select(i => Chunk($"{i * 2:X2} 00 00")));

        Assert.Equal(12, Assert.IsType<ConstructState>(MessageCodec.Read(Kit.Hex(State(chunks: Many(12))))).Chunks.Count);
        Kit.AssertMessageViolation(State(chunks: Many(13)));
    }

    // ----- OperationApplied / IntentSubmit -------------------------------------------------------------------------------------------------------------

    private static string Applied(string changes) => "0A 01  01  01  04 01 05 00 00 00  " + changes;

    [Fact]
    public void Read_OperationAppliedChanges_AreStrictlyAscendingByConstruct()
    {
        Assert.IsType<OperationApplied>(MessageCodec.Read(Kit.Hex(Applied("02  05 01 00  06 01 01"))));
        Assert.IsType<OperationApplied>(MessageCodec.Read(Kit.Hex(Applied("00"))));

        Kit.AssertMessageViolation(Applied("02  05 01 00  05 02 00"));
        Kit.AssertMessageViolation(Applied("02  06 01 00  05 01 00"));
    }

    [Fact]
    public void Read_OperationAppliedWithBadRemovedFlag_ThrowsProtocolViolation() => Kit.AssertMessageViolation(Applied("01  05 01 02"));

    [Fact]
    public void Read_OperationAppliedWithTooManyChanges_ThrowsProtocolViolation()
    {
        var entries = string.Join("  ", Enumerable.Range(1, 1025).Select(i => $"{VarUInt(i)} 01 00"));
        var ok = string.Join("  ", Enumerable.Range(1, 1024).Select(i => $"{VarUInt(i)} 01 00"));

        Assert.Equal(1024, Assert.IsType<OperationApplied>(MessageCodec.Read(Kit.Hex(Applied("80 08  " + ok)))).ConstructChanges.Count);
        Kit.AssertMessageViolation(Applied("81 08  " + entries));
    }

    [Fact]
    public void Read_OperationAppliedWithUnknownOperationKind_ThrowsProtocolViolation()
    {
        Kit.AssertMessageViolation("0A 01 01 01 00 01 00");
        Kit.AssertMessageViolation("0A 01 01 01 08 01 00");
        Kit.AssertMessageViolation("0A 01 01 01 04 02 05 00 00 00 00");
    }

    [Fact]
    public void Read_IntentSubmitWithUnknownIntentCodeOrVersion_ThrowsProtocolViolation()
    {
        Kit.AssertMessageViolation("0B 01 07 00 01 05 00 00 00");
        Kit.AssertMessageViolation("0B 01 07 04 01 05 00 00 00");
        Kit.AssertMessageViolation("0B 01 07 03 02 05 00 00 00");
        Kit.AssertMessageViolation("0B 01 07 03 00 05 00 00 00");
    }

    // ----- ServerHello / registry ------------------------------------------------------------------------------------------------------------------------

    private static string ServerHello(string blocks = "00", string materials = "00", string planets = "00") =>
        "02 01  " + Repeat("00", 16) + "  00 14 01 01  " + planets + "  " + blocks + "  " + materials;

    [Fact]
    public void Read_ServerHello_ControlDecodes()
    {
        var hello = Assert.IsType<ServerHello>(MessageCodec.Read(Kit.Hex(ServerHello("01  01 03 61 62 63", "01  02 01 78"))));

        Assert.Equal(20, hello.TickRate);
        Assert.Equal("abc", Assert.Single(hello.BlockTypes).Key);
        Assert.Equal((ushort)2, Assert.Single(hello.Materials).Id);
    }

    [Theory]
    [InlineData("00", false)]
    [InlineData("81 10", false)]
    [InlineData("80 80 04", false)]
    [InlineData("80 10", true)]
    [InlineData("01", true)]
    public void Read_ServerHelloBlockTypeId_MustBeInOneToMaxBlockTypes(string id, bool valid)
    {
        var hex = ServerHello("01  " + id + " 01 61");

        if (valid)
        {
            Assert.IsType<ServerHello>(MessageCodec.Read(Kit.Hex(hex)));
        }
        else
        {
            Kit.AssertMessageViolation(hex);
        }
    }

    [Theory]
    [InlineData("00", false)]
    [InlineData("80 02", false)]
    [InlineData("FF 01", true)]
    [InlineData("01", true)]
    public void Read_ServerHelloMaterialId_MustBeInOneToMaxMaterials(string id, bool valid)
    {
        var hex = ServerHello(materials: "01  " + id + " 01 61");

        if (valid)
        {
            Assert.IsType<ServerHello>(MessageCodec.Read(Kit.Hex(hex)));
        }
        else
        {
            Kit.AssertMessageViolation(hex);
        }
    }

    [Fact]
    public void Read_ServerHelloRegistryKey_AtAndOverSixtyFourBytes()
    {
        Assert.IsType<ServerHello>(MessageCodec.Read(Kit.Hex(ServerHello("01  01 40 " + Repeat("61", 64)))));

        Kit.AssertMessageViolation(ServerHello("01  01 41 " + Repeat("61", 65)));
        Kit.AssertMessageViolation(ServerHello("01  01 02 C3 28"));
    }

    [Theory]
    [InlineData("41", "00", "00")]
    [InlineData("00", "81 10", "00")]
    [InlineData("00", "00", "80 02")]
    public void Read_ServerHelloListCountsOverLimit_ThrowsProtocolViolation(string planets, string blocks, string materials) =>
        Kit.AssertMessageViolation(ServerHello(blocks, materials, planets));

    // ----- Terrain messages ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Read_TerrainBlockDataLength_AtAndOverSixtyFourKiB()
    {
        var head = "06 01  01 00 00 00 00  05  ";

        var at = Assert.IsType<TerrainBlockData>(MessageCodec.Read(Kit.Hex(head + "80 80 04").Concat(new byte[65536]).ToArray()));
        Assert.Equal(65536, at.Data.Length);

        Kit.AssertViolation(d => MessageCodec.Read(d), Kit.Hex(head + "81 80 04").Concat(new byte[65537]).ToArray());
    }

    [Theory]
    [InlineData("05 01  01  80 20")]
    [InlineData("05 01  01  81 20")]
    public void Read_TerrainCacheReportDeclaringMoreEntriesThanDataOrLimit_ThrowsProtocolViolation(string hex) =>
        Kit.AssertMessageViolation(hex);

    [Fact]
    public void Read_TerrainChecksumsWithTooManyEntries_ThrowsProtocolViolation() => Kit.AssertMessageViolation("07 01  01  81 08");

    [Fact]
    public void Read_TerrainKeyWithBadVarInt_ThrowsProtocolViolation() =>
        Kit.AssertMessageViolation("06 01  01 00 80 80 80 80 10 00 00  05  00");

    // ----- hostile sizes must not be trusted before the limit and remaining-bytes checks ---------------------------------------------------------------

    [Theory]
    [InlineData("05 01 01 80 20")]
    [InlineData("05 01 01 FF FF FF FF 07")]
    [InlineData("07 01 01 80 08")]
    [InlineData("02 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 14 01 01 00 80 10 00")]
    [InlineData("06 01 01 00 00 00 00 05 FF FF FF FF 07")]
    public void Read_HugeDeclaredLengths_DoNotAllocateByTheDeclaredSize(string hex)
    {
        var data = Kit.Hex(hex);
        var warm = Record.Exception(() => MessageCodec.Read(data));
        Assert.IsType<ProtocolViolationException>(warm);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Kit.AssertViolation(d => MessageCodec.Read(d), data);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 32 * 1024, $"allocated {allocated} bytes for a {data.Length}-byte frame");
    }

    [Fact]
    public void Read_ConstructStateDeclaringTwelveChunksWithoutData_DoesNotPreallocateChunkArrays()
    {
        var data = Kit.Hex(State(chunks: "0C"));
        Assert.IsType<ProtocolViolationException>(Record.Exception(() => MessageCodec.Read(data)));

        var before = GC.GetAllocatedBytesForCurrentThread();
        Kit.AssertViolation(d => MessageCodec.Read(d), data);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 32 * 1024, $"allocated {allocated} bytes");
    }

    private static string VarUInt(int value)
    {
        var parts = new List<string>();
        var v = (uint)value;
        do
        {
            var low = (byte)(v & 0x7F);
            v >>= 7;
            parts.Add((v != 0 ? low | 0x80 : low).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        while (v != 0);

        return string.Join(' ', parts);
    }
}
