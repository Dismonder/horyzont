using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K5-24 contract bytes (decisions 1, 12, 13, 14, 16): operation 21, intent 13, reject reason 39 and the enum values.</summary>
public sealed class FlightCodecGoldenTests
{
    // ShipFuelAdd = 21 (0x15), version 1, Construct varuint 5, Units 1000.0 = 447A0000, Capacity 2000.0 = 44FA0000 (little-endian).
    private const string ShipFuelAddHex = "15 01  05  00 00 7A 44  00 00 FA 44";

    // Refuel intent code = 13 (0x0D), version 1, Ship 5, Station 7 (varuint like every intent id), Cell (1, -1, 0) = 02 01 00,
    // RayOrigin (1, 2, 3), RayDirection (0, 0, 1).
    private const string RefuelHex =
        "0D 01  05  07  02 01 00  00 00 80 3F  00 00 00 40  00 00 40 40  00 00 00 00  00 00 00 00  00 00 80 3F";

    private static readonly int[] _launchBlocks = [0, 1, 2];
    private static readonly int[] _threeBits = [0, 1, 2, 4];
    private static readonly int[] _eightBits = [0, 1, 2, 4, 8, 16, 32, 64, 128];

    [Fact]
    public void OperationCodec_ShipFuelAdd_Golden()
    {
        var op = new ShipFuelAddOp(new EntityId(5), 1000f, 2000f);

        Assert.Equal(Kit.Hex(ShipFuelAddHex), Kit.Encode(op));
        DeepAssert.Equal(op, Kit.DecodeOperation(Kit.Hex(ShipFuelAddHex)));
        Assert.Equal(21, (byte)op.Kind);
    }

    [Theory]
    [InlineData("15 01  05  00 00 C0 7F  00 00 FA 44")] // Units NaN
    [InlineData("15 01  05  00 00 00 00  00 00 FA 44")] // Units 0
    [InlineData("15 01  05  00 00 80 BF  00 00 FA 44")] // Units -1
    [InlineData("15 01  05  00 00 7A 44  00 00 80 FF")] // Capacity -inf
    [InlineData("15 01  05  00 00 7A 44  00 00 80 BF")] // Capacity -1
    [InlineData("15 02  05  00 00 7A 44  00 00 FA 44")] // version 2
    [InlineData("15 01  05  00 00 7A 44  00 00 FA")] // truncated
    public void OperationCodec_ShipFuelAdd_RejectsDamaged(string hex) => Kit.AssertOperationViolation(hex);

    [Fact]
    public void IntentCodec_Refuel_Golden()
    {
        var intent = new RefuelIntent(new EntityId(5), new EntityId(7), new CellPos(1, -1, 0), new Vector3(1f, 2f, 3f), new Vector3(0f, 0f, 1f));

        Assert.Equal(Kit.Hex(RefuelHex), Kit.Encode(intent));
        DeepAssert.Equal(intent, Kit.DecodeIntent(Kit.Hex(RefuelHex)));
    }

    [Fact]
    public void RejectReason_ShipNotParked_Golden()
    {
        var reply = new IntentReply(7, RejectReason.ShipNotParked, OpId.None);

        var bytes = Kit.Encode(reply);

        Assert.Equal(39, (byte)RejectReason.ShipNotParked);
        Assert.Equal(new byte[] { 12, 1, 7, 39, 0 }, bytes);
        Assert.Equal(reply, MessageCodec.Read(bytes));
    }

    [Fact]
    public void FlightContract_EnumValues_Golden()
    {
        Assert.Equal(21, (int)OperationKind.ShipFuelAdd);
        Assert.Equal(12, (int)BlobKind.ShipState);
        Assert.Equal(_launchBlocks, Enum.GetValues<ShipLaunchBlock>().Select(v => (int)v));
        Assert.Equal(_threeBits, Enum.GetValues<ShipButtons>().Select(v => (int)v));
        Assert.Equal(_threeBits, Enum.GetValues<ShipStateFlags>().Select(v => (int)v));
        Assert.Equal(_eightBits, Enum.GetValues<ShipFlightFlags>().Select(v => (int)v));
        Assert.Equal(1f, ShipControl.Axis(127));
        Assert.Equal(-1f, ShipControl.Axis(-128));
        Assert.Equal(-1f, ShipControl.Axis(-127));
        Assert.Equal(0f, ShipControl.Axis(0));
    }
}
