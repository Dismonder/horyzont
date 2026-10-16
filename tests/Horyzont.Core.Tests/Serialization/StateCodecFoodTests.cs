using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// K6-33, D6: blob 22 (the progress of a player) in version 3 appends the food to version 2: flag bit 2, and after the bed and the discoveries three slots, each a
/// varuint type and, for a type other than 0, a varuint tick. The flags of version 1 are 1, of version 2 are 3, of version 3 are 7. Written always as version 3.
/// </summary>
public sealed class StateCodecFoodTests
{
    private static readonly PlayerId _player = new(4);
    private static readonly BedRef _bed = new(new EntityId(5), new CellPos(1, -1, 0), 3);
    private static readonly ItemTypeBitSet _five = ItemTypeBitSet.Empty.With(new ItemTypeId(5));

    private static FoodSlot Slot(ushort type, ulong eaten) => new(new ItemTypeId(type), new Tick(eaten));

    private static FoodSlots Food(params FoodSlot[] slots) => new(
        slots.Length > 0 ? slots[0] : default,
        slots.Length > 1 ? slots[1] : default,
        slots.Length > 2 ? slots[2] : default);

    [Fact]
    public void PlayerProgressBlob_V3_GoldenBytes()
    {
        // Food only: flag bit 2, then the three slots (type 5 eaten at tick 100 = 64, and two empty).
        Assert.Equal("16 03 04 04 05 64 00 00", Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(_player, null, null, Food(Slot(5, 100))))));

        // The record of K6-32 without food: only the version byte differs from the bytes of version 2 (16 02 04 03 05 02 01 00 03 01 20).
        Assert.Equal("16 03 04 03 05 02 01 00 03 01 20", Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(_player, _bed, _five))));
        Assert.Equal("16 03 04 01 05 02 01 00 03", Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(_player, _bed))));

        // Everything: the food comes after the bitset. 200 as a varuint is C8 01.
        Assert.Equal(
            "16 03 04 07 05 02 01 00 03 01 20 05 64 06 C8 01 00",
            Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(_player, _bed, _five, Food(Slot(5, 100), Slot(6, 200))))));

        // A filled slot after an empty one, and a filled slot with tick 0.
        Assert.Equal("16 03 04 04 00 06 C8 01 07 00", Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(_player, null, null, Food(default, Slot(6, 200), Slot(7, 0))))));
    }

    [Fact]
    public void PlayerProgressBlob_ReadsV1V2V3()
    {
        // The bytes of K6-30 (version 1) and K6-32 (version 2) still read, with empty food.
        var v1 = StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 01 04 01 05 02 01 00 03"));
        var v2 = StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 02 04 03 05 02 01 00 03 01 20"));
        var v3 = StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 03 04 07 05 02 01 00 03 01 20 05 64 06 C8 01 00"));
        var foodOnly = StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 03 04 04 05 64 00 00"));

        Assert.Equal(_bed, v1.RespawnBed);
        Assert.Null(v1.Discovered);
        Assert.True(v1.Food.IsEmpty);
        Assert.Equal(_bed, v2.RespawnBed);
        Assert.True(v2.Discovered!.Contains(new ItemTypeId(5)));
        Assert.True(v2.Food.IsEmpty);
        Assert.Equal(_bed, v3.RespawnBed);
        Assert.True(v3.Discovered!.Contains(new ItemTypeId(5)));
        Assert.Equal(Food(Slot(5, 100), Slot(6, 200)), v3.Food);
        Assert.Null(foodOnly.RespawnBed);
        Assert.Null(foodOnly.Discovered);
        Assert.Equal(Food(Slot(5, 100)), foodOnly.Food);
    }

    [Fact]
    public void PlayerProgressBlob_RoundTrip_WritesVersion3()
    {
        var many = ItemTypeBitSet.Empty.With(new ItemTypeId(1)).With(new ItemTypeId(9)).With(new ItemTypeId(4096));
        foreach (var record in new[]
        {
            new PlayerProgressRecord(_player, null, null, Food(Slot(5, 100))),
            new PlayerProgressRecord(_player, null, null, Food(default, default, Slot(8, ulong.MaxValue))),
            new PlayerProgressRecord(_player, null, null, Food(Slot(4096, 1), Slot(1, 2), Slot(2, 3))),
            new PlayerProgressRecord(_player, _bed, _five, Food(Slot(5, 100), Slot(6, 200))),
            new PlayerProgressRecord(_player, _bed, many, Food(Slot(7, 0))),
            new PlayerProgressRecord(_player, _bed, _five),
            new PlayerProgressRecord(_player, _bed),
            new PlayerProgressRecord(_player, null, _five),
        })
        {
            var blob = StateCodec.EncodePlayerProgressBlob(record);
            Assert.Equal(3, blob[1]);
            DeepAssert.Equal(record, StateCodec.DecodePlayerProgressBlob(blob));
            Assert.Equal(blob, StateCodec.EncodePlayerProgressBlob(StateCodec.DecodePlayerProgressBlob(blob)));
        }

        // A record read from version 2 is written back as version 3, and nothing but the version byte changes.
        var upgraded = StateCodec.EncodePlayerProgressBlob(StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 02 04 03 05 02 01 00 03 01 20")));
        Assert.Equal("16 03 04 03 05 02 01 00 03 01 20", Kit.ToHex(upgraded));
    }

    [Fact]
    public void FoodBitWithAllEmpty_Throws()
    {
        // The accepted counterpart: one filled slot.
        Assert.Equal(Food(Slot(5, 100)), StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 03 04 04 05 64 00 00")).Food);

        // Bit 2 with three empty slots says "food" and carries none: not canonical, the record without food has no bit 2.
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 04 00 00 00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 07 05 02 01 00 03 01 20 00 00 00"));

        // Bit 2 with the slots missing is a short blob, and food bytes without bit 2 are trailing bytes.
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 04"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 04 05 64 00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 00 05 64 00 00"));
    }

    [Fact]
    public void UnknownFlagBit3_Throws()
    {
        // The accepted counterparts: the flags 0 to 7 of version 3.
        Assert.Equal(_player, StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 03 04 00")).Player);
        Assert.Equal(_player, StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 03 04 04 05 64 00 00")).Player);

        // Bit 3 is unknown in every version; bit 2 is unknown in versions 1 and 2.
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 08"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 0C 05 64 00 00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 80"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 02 04 04 05 64 00 00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 01 04 04 05 64 00 00"));
    }

    [Fact]
    public void Version4_Throws()
    {
        Assert.Equal(_player, StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 03 04 00")).Player);

        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 04 04 00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 00 04 00"));
    }

    [Fact]
    public void FoodTypeAbove4096_Throws()
    {
        // 4096 is the highest valid type id (80 20), 4097 is not (81 20).
        Assert.Equal(Food(Slot(4096, 100)), StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 03 04 04 80 20 64 00 00")).Food);

        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 04 81 20 64 00 00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 03 04 04 00 00 81 20 64"));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(_player, null, null, Food(Slot(4097, 100)))));
    }

    [Fact]
    public void EmptySlotWithTick_Write_Throws()
    {
        StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(_player, null, null, Food(Slot(5, 100))));

        // An empty slot has tick 0 and no tick on the wire.
        Assert.Throws<ArgumentException>(() => StateCodec.EncodePlayerProgressBlob(
            new PlayerProgressRecord(_player, null, null, Food(Slot(5, 100), new FoodSlot(ItemTypeId.None, new Tick(5))))));
    }

    [Fact]
    public void DuplicateFoodTypes_CodecKeepsThemTheBuilderRejects()
    {
        // The codec reads what the bytes say; the world builder is the one that refuses a repeated type or one outside the registry (D6).
        var record = StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 03 04 04 05 64 05 C8 01 00"));

        Assert.Equal(Food(Slot(5, 100), Slot(5, 200)), record.Food);
    }
}
