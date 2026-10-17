using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Intents 20 (<see cref="ItemMoveIntent"/>) and 21 (<see cref="ItemDropIntent"/>), format 1.</summary>
public sealed class IntentCodecItemTests
{
    public static TheoryData<Intent> Intents() =>
    [
        new ItemMoveIntent(new SlotRef(new ContainerId(1), 0), ItemMoveTarget.ToSlot(new SlotRef(new ContainerId(2), 9)), 1, new ItemTypeId(1), 1),
        new ItemMoveIntent(
            new SlotRef(new ContainerId(ulong.MaxValue), ushort.MaxValue), ItemMoveTarget.ToSlot(new SlotRef(new ContainerId(70_000), 255)),
            ushort.MaxValue, new ItemTypeId(4096), ushort.MaxValue),
        new ItemMoveIntent(new SlotRef(new ContainerId(3), 4), ItemMoveTarget.AnyIn(new ContainerId(1)), 12, new ItemTypeId(300), 499),
        new ItemDropIntent(new SlotRef(new ContainerId(1), 39), 1, new ItemTypeId(7), 40),
        new ItemDropIntent(new SlotRef(new ContainerId(ulong.MaxValue), ushort.MaxValue), ushort.MaxValue, new ItemTypeId(4096), ushort.MaxValue),
    ];

    [Theory]
    [MemberData(nameof(Intents))]
    public void RoundTrip(Intent intent)
    {
        var bytes = Kit.Encode(intent);

        Assert.Equal(intent, Kit.DecodeIntent(bytes));
        Assert.Equal(intent is ItemMoveIntent ? 20 : 21, bytes[0]);
        Assert.Equal(1, bytes[1]);
    }

    [Fact]
    public void Move_GoldenBytes()
    {
        var toSlot = new ItemMoveIntent(new SlotRef(new ContainerId(1), 2), ItemMoveTarget.ToSlot(new SlotRef(new ContainerId(3), 4)), 5, new ItemTypeId(6), 7);
        var any = new ItemMoveIntent(new SlotRef(new ContainerId(1), 2), ItemMoveTarget.AnyIn(new ContainerId(3)), 5, new ItemTypeId(6), 7);

        Assert.Equal("14 01 01 02 01 03 04 05 06 07", Kit.ToHex(Kit.Encode(toSlot)));
        Assert.Equal("14 01 01 02 02 03 05 06 07", Kit.ToHex(Kit.Encode(any)));
        Assert.Equal("15 01 01 02 05 06 07", Kit.ToHex(Kit.Encode(new ItemDropIntent(new SlotRef(new ContainerId(1), 2), 5, new ItemTypeId(6), 7))));
    }

    [Theory]
    [InlineData("14 01 01 02 00 03 05 06 07")]
    [InlineData("14 01 01 02 03 03 05 06 07")]
    [InlineData("14 02 01 02 02 03 05 06 07")]
    [InlineData("16 01 01 02 05 06 07")]
    [InlineData("13 01")]
    public void Read_UnknownTargetCodeOrVersion_Throws(string hex) => Kit.AssertIntentViolation(hex);

    [Fact]
    public void Write_NonCanonicalAnyTarget_Throws()
    {
        var intent = new ItemMoveIntent(new SlotRef(new ContainerId(1), 2), new ItemMoveTarget(new ContainerId(3), 4, true), 5, new ItemTypeId(6), 7);

        Assert.Throws<ArgumentException>(() => Kit.Encode(intent));
    }
}
