using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Message 44 (<see cref="PlayerVitalsMessage"/>), format 1 (K6-33 D22): four little-endian <c>f32</c> (health, health maximum, stamina, stamina maximum;
/// finite, 0 to 10 000), a flag byte (bit 0 exhausted, bit 1 the conditions of rest hold, bit 2 rested), a little-endian <c>u16</c> warm-up progress
/// (0 to 1200), a varuint of the remaining ticks of the rest (0 to 64 800) and a comfort byte (0 to 10). Unreliable, server to the owner. Every refusal test first decodes or encodes the valid twin.
/// </summary>
public sealed class MessageCodecVitalsTests
{
    private const MessageType VitalsType = MessageType.PlayerVitals;

    /// <summary>Health 80 of 120, stamina 55.5 of 100, the conditions hold, warm-up 600, nothing remaining, comfort 3.</summary>
    private const string Valid = "2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03";

    private static readonly PlayerVitalsMessage _warming = new(80f, 120f, 55.5f, 100f, false, true, false, 600, 0, 3);

    private static void AssertSame(PlayerVitalsMessage expected, Message actual) => Assert.Equal(expected, Assert.IsType<PlayerVitalsMessage>(actual));

    public static TheoryData<PlayerVitalsMessage> Messages() =>
    [
        new PlayerVitalsMessage(100f, 100f, 100f, 100f, false, false, false, 0, 0, 0),
        new PlayerVitalsMessage(80f, 120f, 55.5f, 100f, false, true, false, 600, 0, 3),
        new PlayerVitalsMessage(0f, 100f, 0f, 100f, true, true, true, 1200, 64_800, 10),
        new PlayerVitalsMessage(10_000f, 10_000f, 10_000f, 10_000f, false, false, true, 0, 1, 0),
        new PlayerVitalsMessage(1f, 140f, 27.75f, 140f, true, false, false, 0, 0, 0),
    ];

    [Theory]
    [MemberData(nameof(Messages))]
    public void PlayerVitals_RoundTrip(PlayerVitalsMessage message)
    {
        var frame = Kit.Encode(message);

        AssertSame(message, MessageCodec.Read(frame));
        Assert.Equal(44, frame[0]);
        Assert.Equal(1, frame[1]);
        Assert.Equal(VitalsType, message.Type);
    }

    [Fact]
    public void PlayerVitals_GoldenBytes()
    {
        Assert.Equal(Valid, Kit.ToHex(Kit.Encode(_warming)));

        // Everything set: exhausted, the conditions hold and rested (flags 07), warm-up 1200 (B0 04), 64 800 ticks remaining (A0 FA 03), comfort 10.
        Assert.Equal(
            "2C 01 00 00 00 00 00 00 C8 42 00 00 00 00 00 00 C8 42 07 B0 04 A0 FA 03 0A",
            Kit.ToHex(Kit.Encode(new PlayerVitalsMessage(0f, 100f, 0f, 100f, true, true, true, 1200, 64_800, 10))));

        // Rested alone (flags 04), 3 600 ticks remaining (90 1C).
        Assert.Equal(
            "2C 01 00 00 C8 42 00 00 C8 42 00 00 C8 42 00 00 C8 42 04 00 00 90 1C 00",
            Kit.ToHex(Kit.Encode(new PlayerVitalsMessage(100f, 100f, 100f, 100f, false, false, true, 0, 3_600, 0))));
    }

    [Fact]
    public void PlayerVitals_Channel_Unreliable()
    {
        // The twin: the frame decodes; the channel is a property of the type (unreliable, 10 Hz, a lost frame is replaced by the next one).
        AssertSame(_warming, MessageCodec.Read(Kit.Hex(Valid)));

        Assert.Equal(DeliveryChannel.Unreliable, MessageChannels.For(VitalsType));
        Assert.True(Kit.Encode(_warming).Length < ProtocolConstants.MaxUnreliableMessageBytes);
    }

    [Theory]
    [InlineData("00 00 C0 7F")]
    [InlineData("00 00 80 7F")]
    [InlineData("00 00 80 FF")]
    public void PlayerVitals_NaN_Throws(string notFinite)
    {
        AssertSame(_warming, MessageCodec.Read(Kit.Hex(Valid)));

        // Each of the four floats in turn: NaN, +infinity and -infinity.
        Kit.AssertMessageViolation("2C 01 " + notFinite + " 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 " + notFinite + " 00 00 5E 42 00 00 C8 42 02 58 02 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 " + notFinite + " 00 00 C8 42 02 58 02 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 " + notFinite + " 02 58 02 00 03");
    }

    [Fact]
    public void PlayerVitals_AboveMax_Throws()
    {
        // 10 000 is the largest value (46 1C 40 00), 10 001 is not; a negative value is refused (-0 is pinned in PlayerVitals_NegativeZero_...).
        AssertSame(new PlayerVitalsMessage(10_000f, 120f, 55.5f, 100f, false, true, false, 600, 0, 3),
            MessageCodec.Read(Kit.Hex("2C 01 00 40 1C 46 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03")));

        Kit.AssertMessageViolation("2C 01 00 44 1C 46 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 44 1C 46 00 00 5E 42 00 00 C8 42 02 58 02 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 44 1C 46 00 00 C8 42 02 58 02 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 44 1C 46 02 58 02 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 80 BF 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 80 BF 00 00 C8 42 02 58 02 00 03");
    }

    [Fact]
    public void PlayerVitals_NegativeZero_ReadAndWrittenAsPositiveZero()
    {
        // -0 (00 00 00 80) is not negative: it is read as 0 and written back as +0 (00 00 00 00), like the player blob (A1 P3-14).
        var message = Assert.IsType<PlayerVitalsMessage>(
            MessageCodec.Read(Kit.Hex("2C 01 00 00 00 80 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03")));
        Assert.Equal(0f, message.Health);

        Assert.Equal(
            "2C 01 00 00 00 00 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03",
            Kit.ToHex(Kit.Encode(message with { Health = -0f })));
    }

    [Fact]
    public void PlayerVitals_InconsistentRestFields_Throws()
    {
        // The twins: a rested frame with remaining ticks, an unrested one without, and a warm-up with the conditions holding.
        AssertSame(
            new PlayerVitalsMessage(80f, 120f, 55.5f, 100f, false, true, true, 0, 3_600, 3),
            MessageCodec.Read(Kit.Hex("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 06 00 00 90 1C 03")));
        AssertSame(_warming, MessageCodec.Read(Kit.Hex(Valid)));

        // Rested with nothing remaining, not rested with ticks remaining, a warm-up without the conditions of rest: states that cannot exist.
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 04 00 00 00 00");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 00 00 00 90 1C 00");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 00 58 02 00 00");

        // The writer refuses them too.
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { Rested = true, RestRemainingTicks = 0 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { RestRemainingTicks = 60 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { RestConditionsMet = false }));
    }

    [Fact]
    public void PlayerVitals_Write_InvalidValues_Throws()
    {
        AssertSame(_warming, MessageCodec.Read(Kit.Encode(_warming)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { Health = float.NaN }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { HealthMax = float.PositiveInfinity }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { Stamina = -1f }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { StaminaMax = 10_001f }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { RestWarmupTicks = ProtocolConstants.MaxRestWarmupTicks + 1 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { Rested = true, RestRemainingTicks = ProtocolConstants.MaxRestRemainingTicks + 1 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { Comfort = 11 }));
    }

    [Fact]
    public void PlayerVitals_UnknownFlag_Throws()
    {
        AssertSame(_warming, MessageCodec.Read(Kit.Hex(Valid)));
        AssertSame(
            new PlayerVitalsMessage(80f, 120f, 55.5f, 100f, true, true, true, 600, 3_600, 3),
            MessageCodec.Read(Kit.Hex("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 07 58 02 90 1C 03")));

        // Bit 3, bit 7 and all bits.
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 0A 58 02 90 1C 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 82 58 02 90 1C 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 FF 58 02 90 1C 03");
    }

    [Fact]
    public void PlayerVitals_WarmupAbove1200_Throws()
    {
        // 1200 (B0 04) is the largest warm-up, 1201 (B1 04) is not.
        AssertSame(
            new PlayerVitalsMessage(80f, 120f, 55.5f, 100f, false, true, false, 1200, 0, 3),
            MessageCodec.Read(Kit.Hex("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 B0 04 00 03")));

        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 B1 04 00 03");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 FF FF 00 03");
    }

    [Fact]
    public void PlayerVitals_RemainingAbove64800OrComfortAbove10_Throws()
    {
        AssertSame(
            new PlayerVitalsMessage(80f, 120f, 55.5f, 100f, false, true, true, 600, 64_800, 10),
            MessageCodec.Read(Kit.Hex("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 06 58 02 A0 FA 03 0A")));

        // 64 801 ticks remaining (A1 FA 03) and comfort 11 (0B).
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 06 58 02 A1 FA 03 0A");
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 06 58 02 A0 FA 03 0B");
    }

    [Fact]
    public void PlayerVitals_TruncatedTrailingOrWrongVersion_Throws()
    {
        AssertSame(_warming, MessageCodec.Read(Kit.Hex(Valid)));

        // Cut after every field, one trailing byte, a future version and version 0.
        foreach (var length in Kit.PrefixLengths(Kit.Hex(Valid).Length))
        {
            Kit.AssertMessageViolation(Kit.ToHex(Kit.Hex(Valid).AsSpan(0, length)));
        }

        Kit.AssertMessageViolation(Valid + " 00");
        Kit.AssertMessageViolation(Valid.Replace("2C 01", "2C 02", StringComparison.Ordinal));
        Kit.AssertMessageViolation(Valid.Replace("2C 01", "2C 00", StringComparison.Ordinal));
    }
}
