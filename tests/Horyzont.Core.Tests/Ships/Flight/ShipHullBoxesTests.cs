using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships.Flight;

/// <summary>K5-24 decision 8: the hull boxes of a ship.</summary>
public sealed class ShipHullBoxesTests
{
    private static void AssertCovers(IEnumerable<CellPos> cells, ShipHullBox[] boxes)
    {
        foreach (var cell in cells)
        {
            Assert.Contains(boxes, b => b.Contains(cell));
        }
    }

    [Fact]
    public void ShipHullBoxes_GreedyMerge_CoversCellsExactly()
    {
        var world = ShipFixtures.CreateWorld(ShipFixtures.Thirty);
        world.Constructs.TryGet(ShipFixtures.ConstructId, out var construct);

        var boxes = ShipHullBoxes.Compute(construct!);

        var cells = ShipFixtures.Thirty.Select(b => b.Cell).ToList();
        AssertCovers(cells, boxes);
        Assert.Equal(cells.Count, boxes.Sum(b => b.Volume));
        Assert.True(boxes.Length < cells.Count, $"{boxes.Length} boxes for {cells.Count} cells");

        var solid = new List<CellPos>();
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                for (var z = -2; z < 4; z++)
                {
                    solid.Add(new CellPos(x, y, z));
                }
            }
        }

        Assert.Equal([new ShipHullBox(new CellPos(0, 0, -2), new CellPos(5, 3, 4))], ShipHullBoxes.Compute(solid));
    }

    [Fact]
    public void ShipHullBoxes_Max128Boxes()
    {
        var checker = new List<CellPos>();
        for (var x = 0; x < 12; x++)
        {
            for (var y = 0; y < 12; y++)
            {
                for (var z = 0; z < 12; z++)
                {
                    if (((x + y + z) & 1) == 0)
                    {
                        checker.Add(new CellPos(x, y, z));
                    }
                }
            }
        }

        var boxes = ShipHullBoxes.Compute(checker);

        Assert.InRange(boxes.Length, 1, ShipHullBoxes.MaxBoxes);
        AssertCovers(checker, boxes);
        Assert.Empty(ShipHullBoxes.Compute([]));
    }
}
