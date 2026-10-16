using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K6-33, format 1 of operation 41 (<see cref="FoodSetOp"/>, D12). Writing refuses with <see cref="ArgumentException"/>, reading with <c>ProtocolViolationException</c>.</summary>
public sealed class OperationCodecFoodTests
{
    private static readonly PlayerId _player = new(4);

    private static FoodSlot Slot(ushort type, ulong eaten) => new(new ItemTypeId(type), new Tick(eaten));

    private static FoodSetOp Op(params FoodSlot[] slots) => new(_player, new FoodSlots(
        slots.Length > 0 ? slots[0] : default,
        slots.Length > 1 ? slots[1] : default,
        slots.Length > 2 ? slots[2] : default));

    [Fact]
    public void FoodSet_GoldenBytes()
    {
        // 0x29 = 41, format 1, the player, then three slots: a type, and for a type other than 0 the tick of eating (both varuint).
        Assert.Equal("29 01 04 05 64 00 00", Kit.ToHex(Kit.Encode(Op(Slot(5, 100)))));
        Assert.Equal("29 01 04 05 64 06 C8 01 07 00", Kit.ToHex(Kit.Encode(Op(Slot(5, 100), Slot(6, 200), Slot(7, 0)))));
        Assert.Equal("29 01 04 00 00 00", Kit.ToHex(Kit.Encode(Op())));
        Assert.Equal("29 01 04 00 06 C8 01 00", Kit.ToHex(Kit.Encode(Op(default, Slot(6, 200)))));

        // The largest player id and the largest tick.
        Assert.Equal(
            "29 01 FF FF FF FF FF FF FF FF FF 01 80 20 FF FF FF FF FF FF FF FF FF 01 00 00",
            Kit.ToHex(Kit.Encode(new FoodSetOp(new PlayerId(ulong.MaxValue), new FoodSlots(Slot(4096, ulong.MaxValue), default, default)))));
    }

    [Fact]
    public void FoodSet_RoundTrip()
    {
        RoundTrip(Op());
        RoundTrip(Op(Slot(5, 100)));
        RoundTrip(Op(default, Slot(6, 200)));
        RoundTrip(Op(default, default, Slot(8, 0)));
        RoundTrip(Op(Slot(1, 1), Slot(2, 2), Slot(3, 3)));
        RoundTrip(Op(Slot(4096, ulong.MaxValue), Slot(4095, 0), Slot(1, 123456789)));
        RoundTrip(new FoodSetOp(new PlayerId(ulong.MaxValue), new FoodSlots(Slot(7, 9), default, default)));
    }

    [Fact]
    public void FoodSet_InACompound_RoundTrip()
    {
        // Eating is a compound of the destroy with the sink Eat and the food (D10); the codec of the compound must carry the new component.
        RoundTrip(new CompoundOp(
        [
            new ItemDestroyOp(new SlotRef(new ContainerId(1), 3), new ItemTypeId(5), 1, ItemSink.Eat),
            Op(Slot(5, 100)),
        ]));
    }

    [Fact]
    public void FoodSet_TypeAbove4096_Throws()
    {
        // The accepted counterpart: 4096 is the highest valid type id, 4097 is not. 4096 as a varuint is 80 20, 4097 is 81 20.
        RoundTrip(Op(Slot(4096, 100)));
        Assert.IsType<FoodSetOp>(Kit.DecodeOperation(Kit.Hex("29 01 04 80 20 64 00 00")));

        Assert.Throws<ArgumentException>(() => Kit.Encode(Op(Slot(4097, 100))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op(default, default, Slot(ushort.MaxValue, 0))));
        Kit.AssertOperationViolation("29 01 04 81 20 64 00 00");
        Kit.AssertOperationViolation("29 01 04 00 00 81 20 64");
    }

    [Fact]
    public void FoodSet_EmptySlotWithTick_Throws()
    {
        // The accepted counterpart: an empty slot with tick 0 is the single byte 00.
        RoundTrip(Op(Slot(5, 100), default));

        // An empty slot has no tick on the wire, so writing a tick for it is refused rather than dropped silently.
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op(new FoodSlot(ItemTypeId.None, new Tick(5)))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op(Slot(5, 100), new FoodSlot(ItemTypeId.None, new Tick(1)))));
    }

    [Fact]
    public void FoodSet_TrailingBytes_Throws()
    {
        Assert.IsType<FoodSetOp>(Kit.DecodeOperation(Kit.Hex("29 01 04 05 64 00 00")));

        Kit.AssertOperationViolation("29 01 04 05 64 00 00 00");
        Kit.AssertOperationViolation("29 01 04 00 00 00 05");

        // A frame that stops early: after the player, inside a slot, before the tick.
        Kit.AssertOperationViolation("29 01 04");
        Kit.AssertOperationViolation("29 01 04 05 64 00");
        Kit.AssertOperationViolation("29 01 04 05");
        Kit.AssertOperationViolation("29 01");
    }

    [Fact]
    public void FoodSet_UnknownFormatVersion_Throws()
    {
        Assert.IsType<FoodSetOp>(Kit.DecodeOperation(Kit.Hex("29 01 04 00 00 00")));

        Kit.AssertOperationViolation("29 00 04 00 00 00");
        Kit.AssertOperationViolation("29 02 04 00 00 00");
    }

    private static void RoundTrip(Operation op)
    {
        var bytes = Kit.Encode(op);
        var decoded = Kit.DecodeOperation(bytes);
        DeepAssert.Equal(op, decoded);
        Assert.Equal(bytes, Kit.Encode(decoded));
    }
}
