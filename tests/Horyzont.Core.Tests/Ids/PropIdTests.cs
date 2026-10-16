using Horyzont.Core.Ids;
using Horyzont.Core.Tests.Props;

namespace Horyzont.Core.Tests.Ids;

/// <summary>K6-31 D4: the packing of <see cref="PropId"/> and <see cref="PropRegionKey"/>.</summary>
public sealed class PropIdTests
{
    private static readonly int[] _cells = [0, 1, 7, 8, 9, 783, 1000, PropId.MaxCellsPerFace - 1];

    [Fact]
    public void Create_RoundTripsFaceCellsSlot()
    {
        foreach (var face in Enumerable.Range(0, 6))
        {
            foreach (var u in _cells)
            {
                foreach (var v in _cells)
                {
                    foreach (var slot in Enumerable.Range(0, 4))
                    {
                        var id = PropId.Create(face, u, v, slot);

                        Assert.Equal(PropFixture.Raw(face, u, v, slot), id);
                        Assert.Equal((face, u, v, slot), (id.Face, id.CellU, id.CellV, id.Slot));
                        Assert.True(id.IsValid);
                    }
                }
            }
        }
    }

    [Fact]
    public void Region_IsHighBits_ContiguousInOrder()
    {
        var ids = new List<PropId>();
        foreach (var face in new[] { 0, 1, 5 })
        {
            for (var u = 0; u < 24; u++)
            {
                for (var v = 0; v < 24; v++)
                {
                    for (var slot = 0; slot < 4; slot++)
                    {
                        ids.Add(PropId.Create(face, u, v, slot));
                    }
                }
            }
        }

        var sorted = ids.OrderBy(id => id.Value).ToList();

        // The region key is the value shifted right by 24 bits, and the ids of one region form one run of the sorted list.
        Assert.All(sorted, id => Assert.Equal(new PropRegionKey(id.Value >> 24), id.Region));
        var runs = sorted.Select(id => id.Region.Value).Distinct().ToList();
        Assert.Equal(3 * 3 * 3, runs.Count);
        Assert.Equal(runs.Order(), runs);
        Assert.Equal(8 * 8 * 4, sorted.GroupBy(id => id.Region).Select(g => g.Count()).Distinct().Single());
        var key = PropId.Create(1, 17, 9, 2).Region;
        Assert.Equal((1, 2, 1), (key.Face, key.RegionU, key.RegionV));
    }

    [Fact]
    public void ReservedBitsSet_Invalid()
    {
        var valid = PropId.Create(3, 123, 456, 2);
        Assert.True(valid.IsValid);

        // Bits 63-61 and 15-0 are reserved.
        foreach (var bit in new[] { 63, 62, 61, 15, 8, 0 })
        {
            Assert.False(new PropId(valid.Value | (1UL << bit)).IsValid, $"bit {bit}");
        }
    }

    [Fact]
    public void FaceAbove5_Invalid()
    {
        Assert.True(new PropId(5UL << 58).IsValid);

        Assert.False(new PropId(6UL << 58).IsValid);
        Assert.False(new PropId(7UL << 58).IsValid);
    }

    [Fact]
    public void MaxGrid_FitsBelowTwoPow61()
    {
        var max = PropId.Create(5, PropId.MaxCellsPerFace - 1, PropId.MaxCellsPerFace - 1, 3);

        Assert.True(max.IsValid);
        Assert.True(max.Value < 1UL << 61);
        Assert.Equal((5, PropId.MaxCellsPerFace - 1, PropId.MaxCellsPerFace - 1, 3), (max.Face, max.CellU, max.CellV, max.Slot));
    }
}
