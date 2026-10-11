using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K5-25 decisions 11 and 14: ShipInputBatch (30) and ShipCorrection (34) bytes and rejections.</summary>
public sealed class ShipInputCodecTests
{
    // Type 30 (0x1E), version 1, FirstSeq u32 7, Count 1, ClientTick u32 100,
    // axes ThrustX 1, ThrustY -1, ThrustZ 127, Pitch -127, Yaw 0, Roll 5, Buttons Assist | LandingGear = 5.
    private const string BatchHex = "1E 01  07 00 00 00  01  64 00 00 00  01 FF 7F 81 00 05  05";

    // Type 34 (0x22), version 1, Ship varuint 5, Seq u32 10, ParentSeq u32 2, Position (1, 2, 3), Rotation identity,
    // LinearVelocity (0, 0, 1), AngularVelocity 0, Fuel 100, Flags Assist | Parked = 09, Gate 0, LaunchBlock 2, HCap +inf.
    private const string CorrectionHex =
        "22 01  05  0A 00 00 00  02 00 00 00  00 00 80 3F 00 00 00 40 00 00 40 40  00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F"
        + "  00 00 00 00 00 00 00 00 00 00 80 3F  00 00 00 00 00 00 00 00 00 00 00 00  00 00 C8 42  09  00  02  00 00 80 7F";

    private static ShipCorrection Correction(float cap = float.PositiveInfinity) => new(
        new EntityId(5), 10, 2, new Vector3(1f, 2f, 3f), Quaternion.Identity, new Vector3(0f, 0f, 1f), Vector3.Zero, 100f,
        ShipFlightFlags.Assist | ShipFlightFlags.Parked, ShipTerrainGate.Ready, ShipLaunchBlock.PersonInHull, cap);

    [Fact]
    public void MessageCodec_ShipInputBatch_GoldenAndRejects()
    {
        var batch = new ShipInputBatch(7, [new ShipInput(100, new ShipControl(1, -1, 127, -127, 0, 5, ShipButtons.Assist | ShipButtons.LandingGear))]);

        Assert.Equal(Kit.Hex(BatchHex), Kit.Encode(batch));
        DeepAssert.Equal(batch, MessageCodec.Read(Kit.Hex(BatchHex)));
        Assert.Equal(DeliveryChannel.Unreliable, MessageChannels.For(MessageType.ShipInputBatch));
        Assert.Equal(30, (byte)MessageType.ShipInputBatch);
    }

    [Theory]
    [InlineData("1E 01  07 00 00 00  00")] // count 0
    [InlineData("1E 01  07 00 00 00  06  64 00 00 00  01 FF 7F 81 00 05  05")] // count 6
    [InlineData("1E 01  07 00 00 00  01  64 00 00 00  80 FF 7F 81 00 05  05")] // axis -128
    [InlineData("1E 01  07 00 00 00  01  64 00 00 00  01 FF 7F 81 00 05  08")] // button bit 8
    [InlineData("1E 01  07 00 00 00  02  64 00 00 00  01 FF 7F 81 00 05  05")] // second input missing
    [InlineData("1E 01  07 00 00 00  01  64 00 00 00  01 FF 7F 81 00 05  05  00")] // trailing byte
    public void MessageCodec_ShipInputBatch_RejectsDamaged(string hex) => Kit.AssertMessageViolation(hex);

    [Fact]
    public void MessageCodec_ShipInputBatch_WriterRefusesBadBatches()
    {
        var input = new ShipInput(1, default);
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ShipInputBatch(1, [])));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ShipInputBatch(1, [input, input, input, input, input, input])));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ShipInputBatch(1, [new ShipInput(1, new ShipControl(-128, 0, 0, 0, 0, 0, 0))])));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ShipInputBatch(1, [new ShipInput(1, new ShipControl(0, 0, 0, 0, 0, 0, (ShipButtons)8))])));
    }

    [Fact]
    public void MessageCodec_ShipCorrection_GoldenAndRejects()
    {
        var correction = Correction();

        Assert.Equal(Kit.Hex(CorrectionHex), Kit.Encode(correction));
        DeepAssert.Equal(correction, MessageCodec.Read(Kit.Hex(CorrectionHex)));
        Assert.Equal(DeliveryChannel.Unreliable, MessageChannels.For(MessageType.ShipCorrection));
        Assert.Equal(34, (byte)MessageType.ShipCorrection);

        // With a two-byte ship id: 75 B of message payload as R13 counts it (type and format version included, transport header
        // excluded) = 2 B type and version + 73 B body (R12).
        var wide = Kit.Encode(correction with { Ship = new EntityId(300) });
        Assert.Equal(75, wide.Length);
        Assert.Equal(12.5f, ((ShipCorrection)MessageCodec.Read(Kit.Encode(Correction(12.5f)))).HorizontalSpeedCap);
    }

    [Theory]
    [InlineData("00 00 00 00")] // HCap 0
    [InlineData("00 00 80 BF")] // HCap -1
    [InlineData("00 00 C0 7F")] // HCap NaN
    [InlineData("00 00 80 FF")] // HCap -inf
    public void MessageCodec_ShipCorrection_RejectsBadCap(string cap) =>
        Kit.AssertMessageViolation(CorrectionHex[..^"00 00 80 7F".Length] + cap);

    [Theory]
    [InlineData("00 00 C8 42  09  00  03", "LaunchBlock 3")]
    [InlineData("00 00 C8 42  09  04  02", "Gate 4")]
    [InlineData("00 00 80 BF  09  00  02", "Fuel -1")]
    public void MessageCodec_ShipCorrection_RejectsBadTail(string tail, string what)
    {
        var hex = CorrectionHex.Replace("00 00 C8 42  09  00  02", tail, StringComparison.Ordinal);
        Assert.NotEqual(CorrectionHex, hex);
        Assert.False(string.IsNullOrEmpty(what));
        Kit.AssertMessageViolation(hex);
    }

    [Fact]
    public void MessageCodec_ShipCorrection_FlagsByte_EveryBitDefined()
    {
        // The reader cannot refuse undefined flight flag bits because there are none; a ninth flag or a freed bit must add the check.
        var all = Enum.GetValues<ShipFlightFlags>().Aggregate(ShipFlightFlags.None, (a, f) => a | f);
        Assert.Equal(0xFF, (byte)all);
        var hex = CorrectionHex.Replace("00 00 C8 42  09  00  02", "00 00 C8 42  FF  00  02", StringComparison.Ordinal);
        Assert.NotEqual(CorrectionHex, hex);
        Assert.Equal(all, ((ShipCorrection)MessageCodec.Read(Kit.Hex(hex))).Flags);
    }

    [Fact]
    public void MessageCodec_ShipCorrection_RejectsDamagedPose()
    {
        // Rotation (0, 0, 0, 2): not normalized.
        Kit.AssertMessageViolation(CorrectionHex.Replace("00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F", "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 40", StringComparison.Ordinal));

        // Position x NaN.
        Kit.AssertMessageViolation(CorrectionHex.Replace("00 00 80 3F 00 00 00 40 00 00 40 40", "00 00 C0 7F 00 00 00 40 00 00 40 40", StringComparison.Ordinal));

        // Truncated.
        Kit.AssertMessageViolation(CorrectionHex[..^3]);
    }

    [Fact]
    public void MessageCodec_ShipCorrection_WriterRefusesBadValues()
    {
        Assert.Throws<ArgumentException>(() => Kit.Encode(Correction(0f)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Correction(float.NaN)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Correction() with { Fuel = -1f }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Correction() with { LaunchBlock = (ShipLaunchBlock)3 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Correction() with { Gate = (ShipTerrainGate)4 }));
    }
}
