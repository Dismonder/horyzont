using Horyzont.Core.Ids;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Tests.Progress;

/// <summary>K6-33, D4: the three food slots as a value.</summary>
public sealed class FoodSlotsTests
{
    private static readonly FoodSlot _berries = new(new ItemTypeId(5), new Tick(100));
    private static readonly FoodSlot _jam = new(new ItemTypeId(6), new Tick(200));

    [Fact]
    public void Empty_IsEmpty()
    {
        Assert.True(FoodSlots.Empty.IsEmpty);
        Assert.Equal(default, FoodSlots.Empty);
        Assert.True(default(FoodSlot).IsEmpty);
        Assert.Equal(3, FoodSlots.Count);
        for (var i = 0; i < FoodSlots.Count; i++)
        {
            Assert.True(FoodSlots.Empty[i].IsEmpty);
        }

        Assert.False(_berries.IsEmpty);
        Assert.False(new FoodSlots(default, default, _berries).IsEmpty);
    }

    [Fact]
    public void With_ReplacesOneSlot()
    {
        var slots = FoodSlots.Empty.With(1, _berries);

        Assert.True(slots[0].IsEmpty);
        Assert.Equal(_berries, slots[1]);
        Assert.True(slots[2].IsEmpty);
        Assert.False(slots.IsEmpty);

        // The value is immutable: the original is unchanged, and each index replaces its own slot.
        Assert.True(FoodSlots.Empty.IsEmpty);
        var all = FoodSlots.Empty.With(0, _berries).With(1, _jam).With(2, _berries);
        Assert.Equal(new FoodSlots(_berries, _jam, _berries), all);
        Assert.Equal(new FoodSlots(_jam, _jam, _berries), all.With(0, _jam));
        Assert.True(all.With(1, default)[1].IsEmpty);
    }

    [Fact]
    public void Equality_ByValue()
    {
        var a = new FoodSlots(_berries, _jam, default);
        var b = new FoodSlots(new FoodSlot(new ItemTypeId(5), new Tick(100)), new FoodSlot(new ItemTypeId(6), new Tick(200)), default);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new FoodSlots(_jam, _berries, default));
        Assert.NotEqual(a, new FoodSlots(_berries, _jam with { Eaten = new Tick(201) }, default));
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var slots = new FoodSlots(_berries, _jam, default);

        // The accepted counterparts first.
        Assert.Equal(_berries, slots[0]);
        Assert.Equal(_jam, slots[1]);
        Assert.True(slots[2].IsEmpty);

        Assert.Throws<ArgumentOutOfRangeException>(() => slots[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => slots[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => slots.With(-1, _berries));
        Assert.Throws<ArgumentOutOfRangeException>(() => slots.With(3, _berries));
    }
}
