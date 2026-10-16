using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Intent 24 (<see cref="CraftIntent"/>), format 1 (K6-32 D12): recipe key (varuint length, UTF-8), varuint count, a flag byte 0 or 1 and, with 1, the
/// block (varuint construct, cell). Every refusal test first decodes or encodes the valid twin, because an unknown intent is refused by the codec
/// today and the refusal alone would prove nothing. The codec checks at least 1 piece; the range 1 to 50 is the validator's.
/// </summary>
public sealed class IntentCodecCraftTests
{
    private static readonly CellPos _cell = new(1, -1, 0);

    public static TheoryData<Intent> Intents() => new()
    {
        { new CraftIntent("t:a", 2, null) },
        { new CraftIntent("t:a", 1, new BlockRef(new EntityId(5), _cell)) },
        { new CraftIntent("core:iron_ingot", 50, new BlockRef(new EntityId(70_000), new CellPos(-2048, 2047, -1))) },
        { new CraftIntent("core:multitool_t2", ushort.MaxValue, new BlockRef(new EntityId(ulong.MaxValue), new CellPos(2047, -2048, 17))) },
        { new CraftIntent(new string('x', 64), 1, null) },
        { new CraftIntent("Zażółć", 3, null) },
    };

    [Theory]
    [MemberData(nameof(Intents))]
    public void RoundTrip(Intent intent)
    {
        var bytes = Kit.Encode(intent);

        Assert.Equal(intent, Kit.DecodeIntent(bytes));
        Assert.Equal(24, bytes[0]);
        Assert.Equal(1, bytes[1]);
    }

    [Fact]
    public void GoldenBytes()
    {
        Assert.Equal("18 01 03 74 3A 61 02 00", Kit.ToHex(Kit.Encode(new CraftIntent("t:a", 2, null))));
        Assert.Equal(
            "18 01 03 74 3A 61 01 01 05 02 01 00",
            Kit.ToHex(Kit.Encode(new CraftIntent("t:a", 1, new BlockRef(new EntityId(5), _cell)))));
        Assert.Equal("18 01 03 74 3A 61 32 00", Kit.ToHex(Kit.Encode(new CraftIntent("t:a", 50, null))));
    }

    [Fact]
    public void RecipeKeyOutside1To64Bytes_Write_Throws()
    {
        var valid = new CraftIntent(new string('x', 64), 1, null);
        Assert.Equal(valid, Kit.DecodeIntent(Kit.Encode(valid)));

        // 65 bytes, 33 two-byte letters (the limit counts UTF-8 bytes), an empty key and a lone surrogate (no UTF-8 form).
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Recipe = new string('x', 65) }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Recipe = new string('ż', 33) }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Recipe = string.Empty }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Recipe = "a\ud800b" }));
    }

    [Fact]
    public void CountZero_Write_Throws()
    {
        var valid = new CraftIntent("t:a", 1, null);
        Assert.Equal(valid, Kit.DecodeIntent(Kit.Encode(valid)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Count = 0 }));
    }

    [Fact]
    public void CountZero_Read_Throws()
    {
        Assert.Equal(new CraftIntent("t:a", 1, null), Kit.DecodeIntent(Kit.Hex("18 01 03 74 3A 61 01 00")));

        Kit.AssertIntentViolation("18 01 03 74 3A 61 00 00");
    }

    [Fact]
    public void CountAboveUnsignedShort_Read_Throws()
    {
        Assert.Equal(new CraftIntent("t:a", ushort.MaxValue, null), Kit.DecodeIntent(Kit.Hex("18 01 03 74 3A 61 FF FF 03 00")));

        // 65536 and 70000 as varuints.
        Kit.AssertIntentViolation("18 01 03 74 3A 61 80 80 04 00");
        Kit.AssertIntentViolation("18 01 03 74 3A 61 F0 A2 04 00");
    }

    [Fact]
    public void UnknownStationFlag_Read_Throws()
    {
        Assert.Equal(new CraftIntent("t:a", 2, null), Kit.DecodeIntent(Kit.Hex("18 01 03 74 3A 61 02 00")));
        Assert.Equal(
            new CraftIntent("t:a", 2, new BlockRef(new EntityId(5), _cell)), Kit.DecodeIntent(Kit.Hex("18 01 03 74 3A 61 02 01 05 02 01 00")));

        Kit.AssertIntentViolation("18 01 03 74 3A 61 02 02");
        Kit.AssertIntentViolation("18 01 03 74 3A 61 02 FF 05 02 01 00");
    }

    [Fact]
    public void Read_InvalidKeyVersionAndTrailingBytes_Throws()
    {
        Assert.Equal(new CraftIntent("t:a", 2, null), Kit.DecodeIntent(Kit.Hex("18 01 03 74 3A 61 02 00")));

        // An empty key, a key of 65 bytes, invalid UTF-8, a key longer than the frame, a future version, a cut station, trailing bytes.
        Kit.AssertIntentViolation("18 01 00 02 00");
        Kit.AssertIntentViolation("18 01 41 " + string.Join(' ', Enumerable.Repeat("78", 65)) + " 01 00");
        Kit.AssertIntentViolation("18 01 02 C3 28 01 00");
        Kit.AssertIntentViolation("18 01 06 74 3A 61 01 00");
        Kit.AssertIntentViolation("18 02 03 74 3A 61 02 00");
        Kit.AssertIntentViolation("18 01 03 74 3A 61 02 01 05");
        Kit.AssertIntentViolation("18 01 03 74 3A 61 02 00 00");
    }
}
