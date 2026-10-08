using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Malformed and boundary frames for <see cref="OperationCodec"/> and <see cref="IntentCodec"/>.</summary>
public sealed class OperationIntentEdgeCaseTests
{
    private const string Zero12 = "00 00 00 00 00 00 00 00 00 00 00 00";
    private const string IdentityRotation = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F";

    private static string TerrainEdit(string shape = "01", string mode = "02", string radius = "00 00 20 40") =>
        "01 01  07  " + shape + "  00 00 80 3F  00 00 00 C0  00 00 00 3F  " + radius + "  " + mode + "  03";

    private static string ConstructCreate(string parent = "02 09", string rotation = IdentityRotation, string block = "01 00 00 00") =>
        "02 01  05  " + parent + "  " + Zero12 + "  " + rotation + "  00 00 00  " + block;

    private static string PlayerJoin(string platform = "02", string name = "01 61", string frame = "02 09", string rotation = IdentityRotation) =>
        "05 01  07  " + platform + "  44 33 22 11 00 00 00 00  " + name + "  09  " + frame + "  00  " + Zero12 + "  " + rotation;

    private static string Reparent(string seat = "00", string parent = "02 09") =>
        "07 01  05  " + parent + "  00  " + Zero12 + "  " + IdentityRotation + "  " + Zero12 + "  " + seat;

    private static string TerrainTool(string mode = "02") => "01 01  07  " + Zero12 + "  " + Zero12 + "  00 00 80 3F  " + mode + "  03";

    private static string PlaceBlock(string target = "01  05  00 00 00  02", string type = "0A", string orientation = "03") =>
        "02 01  " + target + "  " + type + "  " + orientation;

    // ----- operations ------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("00 01 05")]
    [InlineData("08 01 05")]
    [InlineData("FF 01 05")]
    [InlineData("04 00 05 00 00 00")]
    [InlineData("04 02 05 00 00 00")]
    [InlineData("04 FF 05 00 00 00")]
    [InlineData("")]
    [InlineData("04")]
    public void Read_UnknownOperationKindOrVersionOrEmpty_ThrowsProtocolViolation(string hex) => Kit.AssertOperationViolation(hex);

    [Fact]
    public void Read_ControlFramesDecode()
    {
        Assert.IsType<TerrainEditOp>(Kit.DecodeOperation(Kit.Hex(TerrainEdit())));
        Assert.IsType<ConstructCreateOp>(Kit.DecodeOperation(Kit.Hex(ConstructCreate())));
        Assert.IsType<PlayerJoinOp>(Kit.DecodeOperation(Kit.Hex(PlayerJoin())));
        Assert.IsType<EntityReparentOp>(Kit.DecodeOperation(Kit.Hex(Reparent())));
        Assert.IsType<BlockClearOp>(Kit.DecodeOperation(Kit.Hex("04 01 05 00 00 00")));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("02")]
    [InlineData("FF")]
    public void Read_TerrainEditWithUnknownShape_ThrowsProtocolViolation(string shape) => Kit.AssertOperationViolation(TerrainEdit(shape: shape));

    [Theory]
    [InlineData("00")]
    [InlineData("03")]
    [InlineData("FF")]
    public void Read_TerrainEditWithUnknownMode_ThrowsProtocolViolation(string mode) => Kit.AssertOperationViolation(TerrainEdit(mode: mode));

    [Fact]
    public void Read_TerrainEditWithNegativeRadius_IsAcceptedBecauseGameRulesBelongToTheValidator()
    {
        var op = Assert.IsType<TerrainEditOp>(Kit.DecodeOperation(Kit.Hex(TerrainEdit(radius: "00 00 20 C0"))));

        Assert.Equal(-2.5f, op.Radius);
    }

    [Theory]
    [InlineData("00 09")]
    [InlineData("04 09")]
    [InlineData("FF 09")]
    public void Read_ConstructCreateWithUnknownFrameKind_ThrowsProtocolViolation(string parent) =>
        Kit.AssertOperationViolation(ConstructCreate(parent: parent));

    [Theory]
    [InlineData("00 00 00 40")]
    [InlineData("00 00 00 00")]
    [InlineData("00 00 80 3E")]
    [InlineData("00 00 C0 7F")]
    public void Read_ConstructCreateWithNonUnitRotation_ThrowsProtocolViolation(string w) =>
        Kit.AssertOperationViolation(ConstructCreate(rotation: "00 00 00 00 00 00 00 00 00 00 00 00 " + w));

    [Fact]
    public void Read_ConstructCreateWithRotationSlightlyOffUnit_IsAccepted()
    {
        // W = 1.0005 (0x3F801062): |length - 1| = 5e-4, inside the 1e-3 tolerance.
        var op = Assert.IsType<ConstructCreateOp>(Kit.DecodeOperation(Kit.Hex(ConstructCreate(rotation: "00 00 00 00 00 00 00 00 00 00 00 00 62 10 80 3F"))));

        Assert.Equal(1.0005f, op.Rotation.W, 6);
    }

    [Theory]
    [InlineData("01 00 00 10")]
    [InlineData("00 00 03 00")]
    [InlineData("01 00 18 00")]
    public void Read_ConstructCreateWithMalformedBlock_ThrowsProtocolViolation(string block) =>
        Kit.AssertOperationViolation(ConstructCreate(block: block));

    [Theory]
    [InlineData("00")]
    [InlineData("03")]
    [InlineData("FF")]
    public void Read_PlayerJoinWithUnknownPlatform_ThrowsProtocolViolation(string platform) =>
        Kit.AssertOperationViolation(PlayerJoin(platform: platform));

    [Fact]
    public void Read_PlayerJoinDisplayName_AtAndOverThirtyTwoBytes()
    {
        var at = Assert.IsType<PlayerJoinOp>(Kit.DecodeOperation(Kit.Hex(PlayerJoin(name: "20 " + string.Join(' ', Enumerable.Repeat("61", 32))))));
        Assert.Equal(32, at.DisplayName.Length);

        Kit.AssertOperationViolation(PlayerJoin(name: "21 " + string.Join(' ', Enumerable.Repeat("61", 33))));
        Kit.AssertOperationViolation(PlayerJoin(name: "02 C3 28"));
    }

    [Theory]
    [InlineData("00 09")]
    [InlineData("04 09")]
    public void Read_PlayerJoinWithUnknownFrameKind_ThrowsProtocolViolation(string frame) => Kit.AssertOperationViolation(PlayerJoin(frame: frame));

    [Fact]
    public void Read_PlayerJoinWithNonUnitBodyRotation_ThrowsProtocolViolation() =>
        Kit.AssertOperationViolation(PlayerJoin(rotation: "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 40"));

    [Fact]
    public void Read_EntityReparentSeat_PresentAndAbsent()
    {
        var absent = Assert.IsType<EntityReparentOp>(Kit.DecodeOperation(Kit.Hex(Reparent())));
        Assert.Null(absent.SeatCell);

        var present = Assert.IsType<EntityReparentOp>(Kit.DecodeOperation(Kit.Hex(Reparent(seat: "01  02 01 00"))));
        Assert.Equal(new CellPos(1, -1, 0), present.SeatCell);
    }

    [Theory]
    [InlineData("02")]
    [InlineData("FF")]
    [InlineData("01")]
    public void Read_EntityReparentWithBadOptionalFlagOrMissingSeat_ThrowsProtocolViolation(string seat) =>
        Kit.AssertOperationViolation(Reparent(seat: seat));

    [Fact]
    public void Read_EntityReparentWithUnknownParentKind_ThrowsProtocolViolation() => Kit.AssertOperationViolation(Reparent(parent: "00 09"));

    [Fact]
    public void Read_ConsecutiveFrames_ConsumeExactlyOneFrameEach()
    {
        var first = Kit.Hex("04 01 05 00 00 00");
        var second = Kit.Hex("03 01 06 02 01 00 01 00 03 00");
        var both = Kit.Concat(first, second, [0xEE]);

        var r = new ByteReader(both);
        Assert.IsType<BlockClearOp>(OperationCodec.Read(ref r));
        Assert.Equal(first.Length, r.Position);
        var op = Assert.IsType<BlockSetOp>(OperationCodec.Read(ref r));
        Assert.Equal(new EntityId(6), op.Construct);
        Assert.Equal(1, r.Remaining);
    }

    [Fact]
    public void Read_PlayerLeaveAndJoin_RoundTripExactFloatBits()
    {
        var op = new PlayerLeaveOp(
            new PlayerId(3), new FrameRef(FrameKind.Construct, 9), 4, new Vector3(1.5f, -2.25f, 1e-30f), Quaternion.Identity, -0.5f);

        DeepAssert.Equal(op, Kit.DecodeOperation(Kit.Encode(op)));
    }

    // ----- intents ---------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("00 01 05 00 00 00")]
    [InlineData("04 01 05 00 00 00")]
    [InlineData("FF 01 05 00 00 00")]
    [InlineData("03 00 05 00 00 00")]
    [InlineData("03 02 05 00 00 00")]
    [InlineData("")]
    [InlineData("03")]
    public void Read_UnknownIntentCodeOrVersionOrEmpty_ThrowsProtocolViolation(string hex) => Kit.AssertIntentViolation(hex);

    [Fact]
    public void Read_IntentControlFramesDecode()
    {
        Assert.IsType<TerrainToolIntent>(Kit.DecodeIntent(Kit.Hex(TerrainTool())));
        Assert.IsType<PlaceBlockIntent>(Kit.DecodeIntent(Kit.Hex(PlaceBlock())));
        Assert.IsType<RemoveBlockIntent>(Kit.DecodeIntent(Kit.Hex("03 01 05 00 00 00")));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("03")]
    public void Read_TerrainToolWithUnknownMode_ThrowsProtocolViolation(string mode) => Kit.AssertIntentViolation(TerrainTool(mode));

    [Theory]
    [InlineData("00  05  00 00 00  02")]
    [InlineData("03  05  00 00 00  02")]
    [InlineData("FF  05  00 00 00  02")]
    public void Read_PlaceBlockWithUnknownTargetCode_ThrowsProtocolViolation(string target) => Kit.AssertIntentViolation(PlaceBlock(target));

    [Theory]
    [InlineData("06")]
    [InlineData("FF")]
    public void Read_PlaceBlockWithUnknownFace_ThrowsProtocolViolation(string face) =>
        Kit.AssertIntentViolation(PlaceBlock("01  05  00 00 00  " + face));

    [Fact]
    public void Read_PlaceBlockOnTerrain_DecodesAllThreeVectors()
    {
        var hex = "02 01  02  07  " + "00 00 80 3F  00 00 00 40  00 00 40 40  " + "00 00 00 00  00 00 80 3F  00 00 00 00  " + "00 00 00 3F  00 00 00 00  00 00 00 00  0A  03";

        var intent = Assert.IsType<PlaceBlockIntent>(Kit.DecodeIntent(Kit.Hex(hex)));

        var target = Assert.IsType<TerrainTarget>(intent.Target);
        Assert.Equal(new PlanetId(7), target.Planet);
        Assert.Equal(new Vector3(1, 2, 3), target.RayOrigin);
        Assert.Equal(new Vector3(0, 1, 0), target.RayDirection);
        Assert.Equal(new Vector3(0.5f, 0, 0), target.LookForward);
    }

    [Fact]
    public void Read_PlaceBlockTypeId_RangeIsUShort()
    {
        var max = Assert.IsType<PlaceBlockIntent>(Kit.DecodeIntent(Kit.Hex(PlaceBlock(type: "FF FF 03"))));
        Assert.Equal(ushort.MaxValue, max.BlockTypeId);

        Kit.AssertIntentViolation(PlaceBlock(type: "80 80 04"));
        Kit.AssertIntentViolation(PlaceBlock(type: "80 00"));
    }

    [Fact]
    public void Read_PlaceBlockWithGameRuleViolations_IsStillAValidFrame()
    {
        // Orientation 200, type id 0 and a cell far outside the construct bounds are rules for the validator, not for the codec.
        var hex = PlaceBlock("01  05  80 80 80 01  00 00  02", type: "00", orientation: "C8");

        var intent = Assert.IsType<PlaceBlockIntent>(Kit.DecodeIntent(Kit.Hex(hex)));

        Assert.Equal(200, intent.Orientation);
        Assert.Equal((ushort)0, intent.BlockTypeId);
        Assert.False(Assert.IsType<ConstructFaceTarget>(intent.Target).Cell.IsInBounds);
    }

    [Fact]
    public void Read_ConsecutiveIntentFrames_ConsumeExactlyOneFrameEach()
    {
        var both = Kit.Concat(Kit.Hex("03 01 05 00 00 00"), Kit.Hex(PlaceBlock()));

        var r = new ByteReader(both);
        Assert.IsType<RemoveBlockIntent>(IntentCodec.Read(ref r));
        Assert.IsType<PlaceBlockIntent>(IntentCodec.Read(ref r));
        r.EnsureEnd();
    }
}
