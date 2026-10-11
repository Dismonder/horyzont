using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K5-25 contract bytes (decisions 1, 19, 24): intents 11, 12, 16, reject reasons 32–35 and 38, enum values.</summary>
public sealed class SeatCodecGoldenTests
{
    // SeatEnter = 11 (0x0B), version 1, Construct varuint 5, Cell (1, -1, 0) = 02 01 00, RayOrigin (1, 2, 3), RayDirection (0, 0, 1).
    private const string SeatEnterHex =
        "0B 01  05  02 01 00  00 00 80 3F  00 00 00 40  00 00 40 40  00 00 00 00  00 00 00 00  00 00 80 3F";

    // SeatExit = 12 (0x0C), version 1, Construct varuint 300 = AC 02.
    private const string SeatExitHex = "0C 01  AC 02";

    // SeatEvict = 16 (0x10), the layout of SeatEnter.
    private const string SeatEvictHex =
        "10 01  05  02 01 00  00 00 80 3F  00 00 00 40  00 00 40 40  00 00 00 00  00 00 00 00  00 00 80 3F";

    private static readonly int[] _launchBlocks = [0, 1, 2];

    [Fact]
    public void IntentCodec_SeatEnterExitEvict_Golden()
    {
        var enter = new SeatEnterIntent(new EntityId(5), new CellPos(1, -1, 0), new Vector3(1f, 2f, 3f), new Vector3(0f, 0f, 1f));
        var exit = new SeatExitIntent(new EntityId(300));
        var evict = new SeatEvictIntent(new EntityId(5), new CellPos(1, -1, 0), new Vector3(1f, 2f, 3f), new Vector3(0f, 0f, 1f));

        Assert.Equal(Kit.Hex(SeatEnterHex), Kit.Encode(enter));
        Assert.Equal(Kit.Hex(SeatExitHex), Kit.Encode(exit));
        Assert.Equal(Kit.Hex(SeatEvictHex), Kit.Encode(evict));
        DeepAssert.Equal(enter, Kit.DecodeIntent(Kit.Hex(SeatEnterHex)));
        DeepAssert.Equal(exit, Kit.DecodeIntent(Kit.Hex(SeatExitHex)));
        DeepAssert.Equal(evict, Kit.DecodeIntent(Kit.Hex(SeatEvictHex)));
    }

    [Theory]
    [InlineData("0B 02  05  02 01 00  00 00 80 3F  00 00 00 40  00 00 40 40  00 00 00 00  00 00 00 00  00 00 80 3F")] // version 2
    [InlineData("0B 01  05  02 01 00  00 00 C0 7F  00 00 00 40  00 00 40 40  00 00 00 00  00 00 00 00  00 00 80 3F")] // origin NaN
    [InlineData("10 01  05  02 01 00  00 00 80 3F  00 00 00 40  00 00 40 40  00 00 00 00  00 00 00 00  00 00 80 7F")] // direction +inf
    [InlineData("0B 01  05  02 01 00  00 00 80 3F  00 00 00 40")] // truncated
    [InlineData("0C 01")] // exit truncated
    [InlineData("11 01  05")] // code 17 unassigned
    [InlineData("0E 01  05")] // code 14 (K5-27) unknown here
    public void IntentCodec_SeatIntents_RejectDamaged(string hex) => Kit.AssertIntentViolation(hex);

    [Theory]
    [InlineData(RejectReason.SeatOccupied, 32)]
    [InlineData(RejectReason.SeatOutOfReach, 33)]
    [InlineData(RejectReason.EjectTooFast, 34)]
    [InlineData(RejectReason.NotPilot, 35)]
    [InlineData(RejectReason.ConstructNotEmpty, 38)]
    public void RejectReason_SeatCodes_Golden(RejectReason reason, byte value)
    {
        var reply = new IntentReply(7, reason, OpId.None);

        Assert.Equal(value, (byte)reason);
        Assert.Equal(new byte[] { 12, 1, 7, value, 0 }, Kit.Encode(reply));
        Assert.Equal(reply, MessageCodec.Read(Kit.Encode(reply)));
    }

    [Theory]
    [InlineData(36)]
    public void RejectReason_UnassignedSpaceCode_Rejected(byte value) => Kit.AssertMessageViolation($"0C 01 07 {value:X2} 00");

    [Fact]
    public void MovementMode_Gliding_Golden()
    {
        Assert.Equal(6, (byte)MovementMode.Gliding);
        Assert.Equal(7, Enum.GetValues<MovementMode>().Length);
        Assert.Equal(_launchBlocks, Enum.GetValues<ShipLaunchBlock>().Select(v => (int)v));
        Assert.Equal(2, (byte)ShipLaunchBlock.PersonInHull);
    }
}
