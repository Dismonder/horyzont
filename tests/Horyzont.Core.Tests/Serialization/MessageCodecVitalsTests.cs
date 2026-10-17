using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Survival;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Message 44 (<see cref="PlayerVitalsMessage"/>), format 1 (K6-33 D22): four little-endian <c>f32</c> (health, health maximum, stamina, stamina maximum;
/// finite, 0 to 10 000), a flag byte (bit 0 exhausted, bit 1 the conditions of rest hold, bit 2 rested), a little-endian <c>u16</c> warm-up progress
/// (0 to 1200), a varuint of the remaining ticks of the rest (0 to 64 800) and a comfort byte (0 to 10). Unreliable, server to the owner. Every refusal test first decodes or encodes the valid twin.
/// </summary>
public sealed class MessageCodecVitalsTests
{
    private const MessageType VitalsType = MessageType.PlayerVitals;

    /// <summary>The tail of K6-34 (D18) of a server without hazard content: no environment, every resource 100 of 100.</summary>
    private const string Pair100 = " 00 00 C8 42 00 00 C8 42";

    /// <summary>The flag byte, the three masks and the number of hits of a tail without environment.</summary>
    private const string TailFlags = " 00 00 00 00 00";

    /// <summary>The tail of K6-35 (D21): the tick of the sample and the reparent counter, both varuint; zero for a sample that carries no epoch.</summary>
    private const string Epoch = " 00 00";

    private const string Tail = TailFlags + Pair100 + Pair100 + Pair100 + Pair100 + Pair100 + Epoch;

    /// <summary>The K6-33 part of <see cref="Valid"/>, without the tail.</summary>
    private const string Core = "2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03";

    /// <summary>The environment of the frame of the golden bytes: sheltered, cold and radiation present (09), radiation active (08) and exposed (08), 23 hits.</summary>
    private static readonly VitalsEnvironment _sheltered = new(true, false, false, 0x09, 0x08, 0x08, 23);

    private static readonly SuitResources _suit = new(100f, 100f, 100f, 0f, 75.5f);

    private static readonly SuitResources _capacity = new(100f, 100f, 100f, 100f, 200f);

    private static PlayerVitalsMessage WithTail => _warming with { Environment = _sheltered, Suit = _suit, Capacity = _capacity };

    /// <summary>F32 of a value as the little-endian bytes of the frame.</summary>
    private static string F(float value) => Kit.ToHex(BitConverter.GetBytes(value));

    /// <summary>A tail from the bytes of the masks and the pairs of (value, capacity) of the five resources.</summary>
    private static string TailOf(string flags, string present, string active, string exposed, string hits, params (float Value, float Capacity)[] pairs) =>
        " " + flags + " " + present + " " + active + " " + exposed + " " + hits + string.Concat(pairs.Select(p => " " + F(p.Value) + " " + F(p.Capacity))) + Epoch;

    private static readonly (float, float)[] _pairs = [(100f, 100f), (100f, 100f), (100f, 100f), (0f, 100f), (75.5f, 200f)];


    /// <summary>Health 80 of 120, stamina 55.5 of 100, the conditions hold, warm-up 600, nothing remaining, comfort 3.</summary>
    private const string Valid = "2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail;

    private static readonly PlayerVitalsMessage _warming = TestMessages.Vitals(80f, 120f, 55.5f, 100f, false, true, false, 600, 0, 3);

    private static void AssertSame(PlayerVitalsMessage expected, Message actual) => Assert.Equal(expected, Assert.IsType<PlayerVitalsMessage>(actual));

    public static TheoryData<PlayerVitalsMessage> Messages() =>
    [
        TestMessages.Vitals(100f, 100f, 100f, 100f, false, false, false, 0, 0, 0),
        TestMessages.Vitals(80f, 120f, 55.5f, 100f, false, true, false, 600, 0, 3),
        TestMessages.Vitals(0f, 100f, 0f, 100f, true, true, true, 1200, 64_800, 10),
        TestMessages.Vitals(10_000f, 10_000f, 10_000f, 10_000f, false, false, true, 0, 1, 0),
        TestMessages.Vitals(1f, 140f, 27.75f, 140f, true, false, false, 0, 0, 0),
        TestMessages.Vitals(80f, 120f, 55.5f, 100f, false, true, false, 600, 0, 3, new VitalsEnvironment(true, false, false, 0x09, 0x08, 0x08, 23), new SuitResources(100f, 100f, 100f, 0f, 75.5f), new SuitResources(100f, 100f, 100f, 100f, 200f)),
        TestMessages.Vitals(100f, 100f, 100f, 100f, false, false, false, 0, 0, 0, new VitalsEnvironment(false, true, true, 0x1F, 0x03, 0x01, 26), new SuitResources(0f, 1000f, 0.5f, 999.5f, 1f), new SuitResources(1f, 1000f, 100f, 1000f, 1f)),
        TestMessages.Vitals(100f, 100f, 100f, 100f, false, false, false, 0, 0, 0, new VitalsEnvironment(false, false, false, 0x00, 0x00, 0x00, 0), new SuitResources(0f, 0f, 0f, 0f, 0f), new SuitResources(100f, 100f, 100f, 100f, 100f)),
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
            "2C 01 00 00 00 00 00 00 C8 42 00 00 00 00 00 00 C8 42 07 B0 04 A0 FA 03 0A" + Tail,
            Kit.ToHex(Kit.Encode(TestMessages.Vitals(0f, 100f, 0f, 100f, true, true, true, 1200, 64_800, 10))));

        // Rested alone (flags 04), 3 600 ticks remaining (90 1C).
        Assert.Equal(
            "2C 01 00 00 C8 42 00 00 C8 42 00 00 C8 42 00 00 C8 42 04 00 00 90 1C 00" + Tail,
            Kit.ToHex(Kit.Encode(TestMessages.Vitals(100f, 100f, 100f, 100f, false, false, true, 0, 3_600, 0))));
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
        Kit.AssertMessageViolation("2C 01 " + notFinite + " 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 " + notFinite + " 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 " + notFinite + " 00 00 C8 42 02 58 02 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 " + notFinite + " 02 58 02 00 03" + Tail);
    }

    [Fact]
    public void PlayerVitals_AboveMax_Throws()
    {
        // 10 000 is the largest value (46 1C 40 00), 10 001 is not; a negative value is refused (-0 is pinned in PlayerVitals_NegativeZero_...).
        AssertSame(TestMessages.Vitals(10_000f, 120f, 55.5f, 100f, false, true, false, 600, 0, 3),
            MessageCodec.Read(Kit.Hex("2C 01 00 40 1C 46 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail)));

        Kit.AssertMessageViolation("2C 01 00 44 1C 46 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 44 1C 46 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 44 1C 46 00 00 C8 42 02 58 02 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 44 1C 46 02 58 02 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 80 BF 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 80 BF 00 00 C8 42 02 58 02 00 03" + Tail);
    }

    [Fact]
    public void PlayerVitals_NegativeZero_ReadAndWrittenAsPositiveZero()
    {
        // -0 (00 00 00 80) is not negative: it is read as 0 and written back as +0 (00 00 00 00), like the player blob (A1 P3-14).
        var message = Assert.IsType<PlayerVitalsMessage>(
            MessageCodec.Read(Kit.Hex("2C 01 00 00 00 80 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail)));
        Assert.Equal(0f, message.Health);

        Assert.Equal(
            "2C 01 00 00 00 00 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 58 02 00 03" + Tail,
            Kit.ToHex(Kit.Encode(message with { Health = -0f })));
    }

    [Fact]
    public void PlayerVitals_InconsistentRestFields_Throws()
    {
        // The twins: a rested frame with remaining ticks, an unrested one without, and a warm-up with the conditions holding.
        AssertSame(
            TestMessages.Vitals(80f, 120f, 55.5f, 100f, false, true, true, 0, 3_600, 3),
            MessageCodec.Read(Kit.Hex("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 06 00 00 90 1C 03" + Tail)));
        AssertSame(_warming, MessageCodec.Read(Kit.Hex(Valid)));

        // Rested with nothing remaining, not rested with ticks remaining, a warm-up without the conditions of rest: states that cannot exist.
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 04 00 00 00 00" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 00 00 00 90 1C 00" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 00 58 02 00 00" + Tail);

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
            TestMessages.Vitals(80f, 120f, 55.5f, 100f, true, true, true, 600, 3_600, 3),
            MessageCodec.Read(Kit.Hex("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 07 58 02 90 1C 03" + Tail)));

        // Bit 3, bit 7 and all bits.
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 0A 58 02 90 1C 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 82 58 02 90 1C 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 FF 58 02 90 1C 03" + Tail);
    }

    [Fact]
    public void PlayerVitals_WarmupAbove1200_Throws()
    {
        // 1200 (B0 04) is the largest warm-up, 1201 (B1 04) is not.
        AssertSame(
            TestMessages.Vitals(80f, 120f, 55.5f, 100f, false, true, false, 1200, 0, 3),
            MessageCodec.Read(Kit.Hex("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 B0 04 00 03" + Tail)));

        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 B1 04 00 03" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 02 FF FF 00 03" + Tail);
    }

    [Fact]
    public void PlayerVitals_RemainingAbove64800OrComfortAbove10_Throws()
    {
        AssertSame(
            TestMessages.Vitals(80f, 120f, 55.5f, 100f, false, true, true, 600, 64_800, 10),
            MessageCodec.Read(Kit.Hex("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 06 58 02 A0 FA 03 0A" + Tail)));

        // 64 801 ticks remaining (A1 FA 03) and comfort 11 (0B).
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 06 58 02 A1 FA 03 0A" + Tail);
        Kit.AssertMessageViolation("2C 01 00 00 A0 42 00 00 F0 42 00 00 5E 42 00 00 C8 42 06 58 02 A0 FA 03 0B" + Tail);
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

    [Fact]
    public void PlayerVitals_WithSuitTail_GoldenBytes()
    {
        // The example of the plan (D18): sheltered (01), cold and radiation present (09), radiation active (08) and exposed (08), 23 hits (17); heating, cooling and filter 100 of 100, shield 0 of 100,
        // oxygen 75.5 of 200 (00 00 97 42 and 00 00 48 43).
        const string expected = Core + " 01 09 08 08 17" + Pair100 + Pair100 + Pair100 + " 00 00 00 00 00 00 C8 42 00 00 97 42 00 00 48 43" + Epoch;

        Assert.Equal(expected, Kit.ToHex(Kit.Encode(WithTail)));
        AssertSame(WithTail, MessageCodec.Read(Kit.Hex(expected)));
        Assert.Equal(expected, Core + " " + TailOf("01", "09", "08", "08", "17", _pairs).TrimStart());

        // All three protections (07), every hazard present (1F) and active (1F) and exposed (1F), 26 hits (1A), the largest values.
        var most = _warming with { Environment = new VitalsEnvironment(true, true, true, 0x1F, 0x1F, 0x1F, 26), Suit = new SuitResources(1000f, 1000f, 1000f, 1000f, 1000f), Capacity = new SuitResources(1000f, 1000f, 1000f, 1000f, 1000f) };
        var pair1000 = " 00 00 7A 44 00 00 7A 44";
        Assert.Equal(Core + " 07 1F 1F 1F 1A" + pair1000 + pair1000 + pair1000 + pair1000 + pair1000 + Epoch, Kit.ToHex(Kit.Encode(most)));
    }

    [Fact]
    public void PlayerVitals_WithoutTail_Throws()
    {
        // The twin: the frame with the tail is read. The K6-33 frame without the tail, every prefix of the tail and a tail cut inside a float are errors (the version byte stays 01, ProtocolVersion tells the builds apart).
        AssertSame(_warming, MessageCodec.Read(Kit.Hex(Core + Tail)));

        Kit.AssertMessageViolation(Core);
        Kit.AssertMessageViolation(Core + " 00");
        Kit.AssertMessageViolation(Core + TailFlags);
        Kit.AssertMessageViolation(Core + TailFlags + Pair100);
        Kit.AssertMessageViolation(string.Concat(Core, Tail.AsSpan(0, Tail.Length - 3)));
        Kit.AssertMessageViolation(Core + Tail + " 00");
    }

    [Fact]
    public void PlayerVitals_ActiveNotSubsetOfPresent_Throws()
    {
        // The twin: active 08 is a subset of present 09.
        AssertSame(WithTail, MessageCodec.Read(Kit.Hex(Core + TailOf("01", "09", "08", "08", "17", _pairs))));

        Kit.AssertMessageViolation(Core + TailOf("01", "09", "18", "08", "17", _pairs));
        Kit.AssertMessageViolation(Core + TailOf("01", "00", "01", "00", "17", _pairs));
        Assert.Throws<ArgumentException>(() => Kit.Encode(WithTail with { Environment = _sheltered with { Active = 0x18 } }));
    }

    [Fact]
    public void PlayerVitals_ExposedNotSubsetOfActive_Throws()
    {
        AssertSame(WithTail, MessageCodec.Read(Kit.Hex(Core + TailOf("01", "09", "08", "08", "17", _pairs))));

        // Exposed 01 (cold) is present but not active (the shelter keeps it out): a resource of a blocked hazard cannot hurt.
        Kit.AssertMessageViolation(Core + TailOf("01", "09", "08", "01", "17", _pairs));
        Kit.AssertMessageViolation(Core + TailOf("01", "09", "08", "09", "17", _pairs));
        Assert.Throws<ArgumentException>(() => Kit.Encode(WithTail with { Environment = _sheltered with { Exposed = 0x01 } }));
    }

    [Fact]
    public void PlayerVitals_ValueAboveCapacity_Throws()
    {
        AssertSame(WithTail, MessageCodec.Read(Kit.Hex(Core + TailOf("01", "09", "08", "08", "17", _pairs))));

        // The oxygen 200 of 200 is the limit; 201 of 200 and a negative value are not. The same for each of the five resources in turn.
        Kit.AssertMessageViolation(Core + TailOf("01", "09", "08", "08", "17", (100f, 100f), (100f, 100f), (100f, 100f), (0f, 100f), (201f, 200f)));
        Kit.AssertMessageViolation(Core + TailOf("01", "09", "08", "08", "17", (100f, 100f), (100f, 100f), (100f, 100f), (0f, 100f), (-1f, 200f)));
        AssertSame(WithTail with { Suit = _suit with { Oxygen = 200f } }, MessageCodec.Read(Kit.Hex(Core + TailOf("01", "09", "08", "08", "17", (100f, 100f), (100f, 100f), (100f, 100f), (0f, 100f), (200f, 200f)))));
        Kit.AssertMessageViolation(Core + TailOf("01", "09", "08", "08", "17", (101f, 100f), (100f, 100f), (100f, 100f), (0f, 100f), (75.5f, 200f)));
        Kit.AssertMessageViolation(Core + TailOf("01", "09", "08", "08", "17", (100f, 100f), (100f, 100f), (100f, 100f), (100.5f, 100f), (75.5f, 200f)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(WithTail with { Suit = _suit with { Oxygen = 201f } }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(WithTail with { Suit = _suit with { Filter = -1f } }));
    }

    [Fact]
    public void PlayerVitals_CapacityZeroOrAbove1000_Throws()
    {
        // 1 and 1000 are the limits of a capacity; 0 and 1001 are not (the capacity is the denominator of the bars).
        AssertSame(
            _warming with { Suit = new SuitResources(0f, 0f, 0f, 0f, 0f), Capacity = new SuitResources(1f, 1f, 1000f, 1000f, 1f) },
            MessageCodec.Read(Kit.Hex(Core + TailOf("00", "00", "00", "00", "00", (0f, 1f), (0f, 1f), (0f, 1000f), (0f, 1000f), (0f, 1f)))));

        Kit.AssertMessageViolation(Core + TailOf("00", "00", "00", "00", "00", (0f, 1f), (0f, 1f), (0f, 1000f), (0f, 1000f), (0f, 0f)));
        Kit.AssertMessageViolation(Core + TailOf("00", "00", "00", "00", "00", (0f, 0f), (0f, 1f), (0f, 1000f), (0f, 1000f), (0f, 1f)));
        Kit.AssertMessageViolation(Core + TailOf("00", "00", "00", "00", "00", (0f, 1f), (0f, 1f), (0f, 1000f), (0f, 1001f), (0f, 1f)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { Capacity = _capacity with { Heating = 0f } }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(_warming with { Capacity = _capacity with { Oxygen = 1001f } }));
    }

    [Theory]
    [InlineData("00 00 C0 7F")]
    [InlineData("00 00 80 7F")]
    [InlineData("00 00 80 FF")]
    public void PlayerVitals_NonFiniteSuit_Throws(string notFinite)
    {
        AssertSame(WithTail, MessageCodec.Read(Kit.Hex(Core + TailOf("01", "09", "08", "08", "17", _pairs))));

        // A value and a capacity that are not numbers; the frame is built by hand because the writer refuses them.
        var good = TailOf("01", "09", "08", "08", "17", _pairs);
        Kit.AssertMessageViolation(Core + good.Replace("00 00 97 42", notFinite, StringComparison.Ordinal));
        Kit.AssertMessageViolation(Core + good.Replace("00 00 48 43", notFinite, StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => Kit.Encode(WithTail with { Suit = _suit with { Oxygen = float.NaN } }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(WithTail with { Capacity = _capacity with { Oxygen = float.PositiveInfinity } }));
    }

    [Fact]
    public void PlayerVitals_UnknownEnvironmentFlag_Throws()
    {
        // The three protections are the bits 0 to 2 of the flag byte (07 is all); bit 3 and above are errors.
        AssertSame(WithTail, MessageCodec.Read(Kit.Hex(Core + TailOf("01", "09", "08", "08", "17", _pairs))));
        AssertSame(
            WithTail with { Environment = _sheltered with { Sheltered = true, Generator = true, LifeSupport = true } },
            MessageCodec.Read(Kit.Hex(Core + TailOf("07", "09", "08", "08", "17", _pairs))));

        Kit.AssertMessageViolation(Core + TailOf("08", "09", "08", "08", "17", _pairs));
        Kit.AssertMessageViolation(Core + TailOf("80", "09", "08", "08", "17", _pairs));
        Kit.AssertMessageViolation(Core + TailOf("FF", "09", "08", "08", "17", _pairs));
    }

    [Fact]
    public void PlayerVitals_MaskBits5To7_Throw()
    {
        // 1F is every hazard; the bits 5 to 7 of a mask do not exist. Each of the three masks in turn (the subset rules are met so that only the bit is wrong).
        var all = WithTail with { Environment = new VitalsEnvironment(false, false, false, 0x1F, 0x1F, 0x1F, 0) };
        AssertSame(all, MessageCodec.Read(Kit.Hex(Core + TailOf("00", "1F", "1F", "1F", "00", _pairs))));

        Kit.AssertMessageViolation(Core + TailOf("00", "3F", "1F", "1F", "00", _pairs));
        Kit.AssertMessageViolation(Core + TailOf("00", "FF", "3F", "1F", "00", _pairs));
        Kit.AssertMessageViolation(Core + TailOf("00", "FF", "FF", "20", "00", _pairs));
        Kit.AssertMessageViolation(Core + TailOf("00", "FF", "FF", "FF", "00", _pairs));
        Assert.Throws<ArgumentException>(() => Kit.Encode(WithTail with { Environment = new VitalsEnvironment(false, false, false, 0x20, 0x20, 0x20, 0) }));
    }

    [Fact]
    public void PlayerVitals_HitsAbove26_Throws()
    {
        // 26 (1A) rays is the most; 27 (1B) cannot be.
        AssertSame(WithTail with { Environment = _sheltered with { ShelterHits = 26 } }, MessageCodec.Read(Kit.Hex(Core + TailOf("01", "09", "08", "08", "1A", _pairs))));

        Kit.AssertMessageViolation(Core + TailOf("01", "09", "08", "08", "1B", _pairs));
        Kit.AssertMessageViolation(Core + TailOf("01", "09", "08", "08", "FF", _pairs));
        Assert.Throws<ArgumentException>(() => Kit.Encode(WithTail with { Environment = _sheltered with { ShelterHits = 27 } }));
    }
}
