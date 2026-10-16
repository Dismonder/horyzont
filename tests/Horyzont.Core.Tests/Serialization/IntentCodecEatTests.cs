using Horyzont.Core.Ids;
using Horyzont.Core.Intents;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Intent 25 (<see cref="EatIntent"/>), format 1 (K6-33 D10): varuint container, varuint slot (0 to 65535). The codec checks only the shape; the
/// container kind and the slot range are the validator's. Every refusal test first decodes the valid twin, because an unknown intent is refused by the
/// codec before step B2 and the refusal alone would prove nothing.
/// </summary>
public sealed class IntentCodecEatTests
{
    public static TheoryData<Intent> Intents() =>
    [
        new EatIntent(new ContainerId(2), 3),
        new EatIntent(new ContainerId(1), 0),
        new EatIntent(new ContainerId(128), 39),
        new EatIntent(new ContainerId(70_000), 255),
        new EatIntent(new ContainerId(ulong.MaxValue), ushort.MaxValue),
    ];

    [Theory]
    [MemberData(nameof(Intents))]
    public void Eat_RoundTrip(Intent intent)
    {
        var bytes = Kit.Encode(intent);

        Assert.Equal(intent, Kit.DecodeIntent(bytes));
        Assert.Equal(25, bytes[0]);
        Assert.Equal(1, bytes[1]);
    }

    [Fact]
    public void Eat_GoldenBytes()
    {
        Assert.Equal("19 01 02 03", Kit.ToHex(Kit.Encode(new EatIntent(new ContainerId(2), 3))));
        Assert.Equal("19 01 01 00", Kit.ToHex(Kit.Encode(new EatIntent(new ContainerId(1), 0))));
        Assert.Equal("19 01 80 01 27", Kit.ToHex(Kit.Encode(new EatIntent(new ContainerId(128), 39))));
        Assert.Equal("19 01 02 FF FF 03", Kit.ToHex(Kit.Encode(new EatIntent(new ContainerId(2), ushort.MaxValue))));
    }

    [Fact]
    public void Eat_SlotAboveUnsignedShort_Throws()
    {
        Assert.Equal(new EatIntent(new ContainerId(2), ushort.MaxValue), Kit.DecodeIntent(Kit.Hex("19 01 02 FF FF 03")));

        // 65536 and 70000 as varuints.
        Kit.AssertIntentViolation("19 01 02 80 80 04");
        Kit.AssertIntentViolation("19 01 02 F0 A2 04");
    }

    [Fact]
    public void Eat_TrailingBytes_Throws()
    {
        Assert.Equal(new EatIntent(new ContainerId(2), 3), Kit.DecodeIntent(Kit.Hex("19 01 02 03")));

        Kit.AssertIntentViolation("19 01 02 03 00");
    }

    [Fact]
    public void Eat_TruncatedOrWrongVersion_Throws()
    {
        Assert.Equal(new EatIntent(new ContainerId(2), 3), Kit.DecodeIntent(Kit.Hex("19 01 02 03")));

        // The slot missing, the container missing, a future version, version 0 and the frame cut after the code.
        Kit.AssertIntentViolation("19 01 02");
        Kit.AssertIntentViolation("19 01");
        Kit.AssertIntentViolation("19 02 02 03");
        Kit.AssertIntentViolation("19 00 02 03");
        Kit.AssertIntentViolation("19");
    }

    [Fact]
    public void Eat_NeighbouringCodes_StayUnassigned()
    {
        // 25 is the only new code (the spec keeps 26 for the suit refill of K6-34): the valid twin decodes, the neighbour is refused as unknown.
        Assert.Equal(new EatIntent(new ContainerId(2), 3), Kit.DecodeIntent(Kit.Hex("19 01 02 03")));

        Kit.AssertIntentViolation("1A 01 02 03");
    }
}
