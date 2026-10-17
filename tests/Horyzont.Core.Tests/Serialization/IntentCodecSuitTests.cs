using Horyzont.Core.Ids;
using Horyzont.Core.Intents;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Intent 26 (<see cref="SuitRefillIntent"/>), format 1 (K6-34 D7): varuint container, varuint slot (0 to 65535), the shape of intent 25. The codec checks only the shape; the container kind and the slot range are the
/// validator's. Every refusal test first decodes the valid twin, because an unknown intent is refused by the codec before step B2 and the refusal alone would prove nothing.
/// </summary>
public sealed class IntentCodecSuitTests
{
    public static TheoryData<Intent> Intents() =>
    [
        new SuitRefillIntent(new ContainerId(2), 3),
        new SuitRefillIntent(new ContainerId(1), 0),
        new SuitRefillIntent(new ContainerId(128), 39),
        new SuitRefillIntent(new ContainerId(70_000), 255),
        new SuitRefillIntent(new ContainerId(ulong.MaxValue), ushort.MaxValue),
    ];

    [Theory]
    [MemberData(nameof(Intents))]
    public void SuitRefill_RoundTrip(Intent intent)
    {
        var bytes = Kit.Encode(intent);

        Assert.Equal(intent, Kit.DecodeIntent(bytes));
        Assert.Equal(26, bytes[0]);
        Assert.Equal(1, bytes[1]);
    }

    [Fact]
    public void SuitRefill_GoldenBytes()
    {
        Assert.Equal("1A 01 02 03", Kit.ToHex(Kit.Encode(new SuitRefillIntent(new ContainerId(2), 3))));
        Assert.Equal("1A 01 01 00", Kit.ToHex(Kit.Encode(new SuitRefillIntent(new ContainerId(1), 0))));
        Assert.Equal("1A 01 80 01 27", Kit.ToHex(Kit.Encode(new SuitRefillIntent(new ContainerId(128), 39))));
        Assert.Equal("1A 01 02 FF FF 03", Kit.ToHex(Kit.Encode(new SuitRefillIntent(new ContainerId(2), ushort.MaxValue))));
    }

    [Fact]
    public void SuitRefill_SlotAboveUnsignedShort_Throws()
    {
        Assert.Equal(new SuitRefillIntent(new ContainerId(2), ushort.MaxValue), Kit.DecodeIntent(Kit.Hex("1A 01 02 FF FF 03")));

        // 65536 and 70000 as varuints.
        Kit.AssertIntentViolation("1A 01 02 80 80 04");
        Kit.AssertIntentViolation("1A 01 02 F0 A2 04");
    }

    [Fact]
    public void SuitRefill_TrailingBytes_Throws()
    {
        Assert.Equal(new SuitRefillIntent(new ContainerId(2), 3), Kit.DecodeIntent(Kit.Hex("1A 01 02 03")));

        Kit.AssertIntentViolation("1A 01 02 03 00");
    }

    [Fact]
    public void SuitRefill_TruncatedOrWrongVersion_Throws()
    {
        Assert.Equal(new SuitRefillIntent(new ContainerId(2), 3), Kit.DecodeIntent(Kit.Hex("1A 01 02 03")));

        // The slot missing, the container missing, a future version, version 0 and the frame cut after the code.
        Kit.AssertIntentViolation("1A 01 02");
        Kit.AssertIntentViolation("1A 01");
        Kit.AssertIntentViolation("1A 02 02 03");
        Kit.AssertIntentViolation("1A 00 02 03");
        Kit.AssertIntentViolation("1A");
    }

    [Fact]
    public void SuitRefill_IsNotTheEatIntent()
    {
        // 25 and 26 have the same shape and must not be mixed up: the same bytes under the two codes decode to the two records.
        Assert.Equal(new SuitRefillIntent(new ContainerId(2), 3), Kit.DecodeIntent(Kit.Hex("1A 01 02 03")));
        Assert.Equal(new EatIntent(new ContainerId(2), 3), Kit.DecodeIntent(Kit.Hex("19 01 02 03")));
        Assert.NotEqual<Intent>(new EatIntent(new ContainerId(2), 3), new SuitRefillIntent(new ContainerId(2), 3));
    }
}
