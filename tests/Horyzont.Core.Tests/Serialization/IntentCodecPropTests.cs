using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Tests.Props;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Intent 27 (<see cref="PropHitIntent"/>), format 1 (K6-31). Every refusal test first decodes or encodes the valid twin, because an unknown
/// intent code is refused by the codec today and the refusal alone would prove nothing.
/// </summary>
public sealed class IntentCodecPropTests
{
    // Face 0, cell u 9, v 2, slot 0: LEB128 80 80 A0 81 80 40 (the tree of OperationCodecPropTests).
    private static readonly PropId _tree = PropFixture.Raw(0, 9, 2, 0);

    private const string TreeBytes = "80 80 A0 81 80 40";

    public static TheoryData<Intent> Intents() =>
    [
        new PropHitIntent(new PlanetId(1), _tree, 0),
        new PropHitIntent(new PlanetId(1), PropFixture.Raw(1, 0, 0, 1), 255),
        new PropHitIntent(new PlanetId(ulong.MaxValue), PropId.Create(5, PropId.MaxCellsPerFace - 1, PropId.MaxCellsPerFace - 1, 3), 9),
    ];

    [Theory]
    [MemberData(nameof(Intents))]
    public void RoundTrip(Intent intent)
    {
        var bytes = Kit.Encode(intent);

        Assert.Equal(intent, Kit.DecodeIntent(bytes));
        Assert.Equal(27, bytes[0]);
        Assert.Equal(1, bytes[1]);
    }

    [Fact]
    public void GoldenBytes()
    {
        Assert.Equal("1B 01 01 " + TreeBytes + " 03", Kit.ToHex(Kit.Encode(new PropHitIntent(new PlanetId(1), _tree, 3))));
        Assert.Equal("1B 01 07 " + TreeBytes + " FF 01", Kit.ToHex(Kit.Encode(new PropHitIntent(new PlanetId(7), _tree, 255))));
        Assert.Equal(new PropHitIntent(new PlanetId(1), _tree, 3), Kit.DecodeIntent(Kit.Hex("1B 01 01 " + TreeBytes + " 03")));
    }

    [Fact]
    public void ToolSlotOver255_Read_Throws()
    {
        Assert.Equal(new PropHitIntent(new PlanetId(1), _tree, 255), Kit.DecodeIntent(Kit.Hex("1B 01 01 " + TreeBytes + " FF 01")));

        Kit.AssertIntentViolation("1B 01 01 " + TreeBytes + " 80 02");
        Kit.AssertIntentViolation("1B 01 01 " + TreeBytes + " FF FF 03");
    }

    [Fact]
    public void ToolSlotOver255_Write_Throws()
    {
        var valid = new PropHitIntent(new PlanetId(1), _tree, 255);
        Assert.Equal(valid, Kit.DecodeIntent(Kit.Encode(valid)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { ToolSlot = 256 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { ToolSlot = ushort.MaxValue }));
    }

    [Fact]
    public void ReservedBitsPropId_Throws()
    {
        var valid = new PropHitIntent(new PlanetId(1), _tree, 0);
        Assert.Equal(valid, Kit.DecodeIntent(Kit.Encode(valid)));

        // Bit 0 (reserved low bits), bit 61 (reserved high bits) and face 6 are refused on reading; writing them is an error of the caller.
        Kit.AssertIntentViolation("1B 01 01 01 00");
        Kit.AssertIntentViolation("1B 01 01 80 80 80 80 80 80 80 80 20 00");
        Kit.AssertIntentViolation("1B 01 01 80 80 80 80 80 80 80 80 18 00");
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Prop = new PropId(1) }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Prop = new PropId(1UL << 61) }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Prop = new PropId(6UL << 58) }));
    }

    [Fact]
    public void Read_VersionAndTrailingBytes_Throws()
    {
        Assert.Equal(new PropHitIntent(new PlanetId(1), _tree, 3), Kit.DecodeIntent(Kit.Hex("1B 01 01 " + TreeBytes + " 03")));

        // A version from the future, a missing field, trailing bytes.
        Kit.AssertIntentViolation("1B 02 01 " + TreeBytes + " 03");
        Kit.AssertIntentViolation("1B 01 01 " + TreeBytes);
        Kit.AssertIntentViolation("1B 01 01");
        Kit.AssertIntentViolation("1B 01 01 " + TreeBytes + " 03 00");
    }
}
