using Horyzont.Core.Ids;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32 D7: the immutable set of discovered item types and its canonical bytes.</summary>
public sealed class ItemTypeBitSetTests
{
    [Fact]
    public void With_Contains()
    {
        var empty = ItemTypeBitSet.Empty;
        var set = empty.With(new ItemTypeId(5)).With(new ItemTypeId(9)).With(new ItemTypeId(4096));

        Assert.True(empty.IsEmpty);
        Assert.Equal(0, empty.Count);
        Assert.False(empty.Contains(new ItemTypeId(5)));
        Assert.Equal(3, set.Count);
        Assert.False(set.IsEmpty);
        Assert.True(set.Contains(new ItemTypeId(5)));
        Assert.True(set.Contains(new ItemTypeId(9)));
        Assert.True(set.Contains(new ItemTypeId(4096)));
        Assert.False(set.Contains(new ItemTypeId(6)));

        // The type 0 and a type above the limit are never in a set; adding them is refused.
        Assert.False(set.Contains(ItemTypeId.None));
        Assert.False(set.Contains(new ItemTypeId(4097)));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.With(ItemTypeId.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.With(new ItemTypeId(4097)));

        // The set is immutable and adding a present type returns the same set.
        Assert.True(empty.IsEmpty);
        Assert.Same(set, set.With(new ItemTypeId(5)));
    }

    [Fact]
    public void Without_Removes()
    {
        var set = ItemTypeBitSet.Empty.With(new ItemTypeId(5)).With(new ItemTypeId(4096));

        var withoutHigh = set.Without(new ItemTypeId(4096));
        var withoutAll = withoutHigh.Without(new ItemTypeId(5));

        Assert.False(withoutHigh.Contains(new ItemTypeId(4096)));
        Assert.True(withoutHigh.Contains(new ItemTypeId(5)));
        Assert.Equal(1, withoutHigh.Bytes.Length);
        Assert.True(withoutAll.IsEmpty);
        Assert.Equal(ItemTypeBitSet.Empty, withoutAll);
        Assert.True(set.Contains(new ItemTypeId(4096)));
        Assert.Same(set, set.Without(new ItemTypeId(7)));
        Assert.Same(set, set.Without(ItemTypeId.None));
    }

    [Fact]
    public void Equality_ByContent()
    {
        var a = ItemTypeBitSet.Empty.With(new ItemTypeId(3)).With(new ItemTypeId(70));
        var b = ItemTypeBitSet.Empty.With(new ItemTypeId(70)).With(new ItemTypeId(3));
        var c = ItemTypeBitSet.Empty.With(new ItemTypeId(3));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, ItemTypeBitSet.Empty);
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals(null));
        Assert.Equal(a, a.With(new ItemTypeId(9)).Without(new ItemTypeId(9)));
    }

    [Fact]
    public void Bytes_Canonical()
    {
        // Bit i in byte i / 8, least significant bit first: type 5 is 0x20 in byte 0, type 9 is 0x02 in byte 1, type 4096 is 0x01 in byte 512.
        var five = ItemTypeBitSet.Empty.With(new ItemTypeId(5));
        var nine = five.With(new ItemTypeId(9));
        var top = ItemTypeBitSet.Empty.With(new ItemTypeId(4096));

        Assert.Equal(new byte[] { 0x20 }, five.Bytes.ToArray());
        Assert.Equal(new byte[] { 0x20, 0x02 }, nine.Bytes.ToArray());
        Assert.Equal(513, top.Bytes.Length);
        Assert.Equal(1, top.Bytes[512]);
        Assert.Empty(ItemTypeBitSet.Empty.Bytes.ToArray());

        Assert.Equal(nine, ItemTypeBitSet.FromBytes([0x20, 0x02]));
        Assert.Equal(top, ItemTypeBitSet.FromBytes(top.Bytes));
        Assert.Equal(ItemTypeBitSet.Empty, ItemTypeBitSet.FromBytes([]));
    }

    [Fact]
    public void FromBytes_NonCanonical_Throws()
    {
        // The accepted counterpart first: the longest allowed set.
        var longest = new byte[513];
        longest[512] = 1;
        Assert.True(ItemTypeBitSet.FromBytes(longest).Contains(new ItemTypeId(4096)));

        var tooLong = new byte[514];
        tooLong[513] = 1;
        Assert.Throws<ArgumentException>(() => ItemTypeBitSet.FromBytes(tooLong));
        Assert.Throws<ArgumentException>(() => ItemTypeBitSet.FromBytes([0x20, 0x00]));
        Assert.Throws<ArgumentException>(() => ItemTypeBitSet.FromBytes([0x00]));
        Assert.Throws<ArgumentException>(() => ItemTypeBitSet.FromBytes([0x21]));
    }
}
