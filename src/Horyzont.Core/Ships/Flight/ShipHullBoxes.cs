using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Collision proxy of a ship hull (K5-24 decision 8): occupied cells merged greedily into boxes along X, then Z, then Y. Slabs and
/// slopes count as full cells. More than <see cref="MaxBoxes"/> boxes → the occupancy is coarsened (2× cells) and merged again, so
/// the cover may exceed the cells. Pure; computed on entering the ship mode and on block changes, not every tick.
/// </summary>
public static class ShipHullBoxes
{
    /// <summary>Largest number of boxes.</summary>
    public const int MaxBoxes = 128;

    /// <summary>Computes the boxes of a construct.</summary>
    /// <param name="construct">The construct.</param>
    /// <returns>The boxes; empty for an empty construct.</returns>
    public static ShipHullBox[] Compute(Construct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        var cells = new List<CellPos>();
        foreach (var (chunkPos, chunk) in construct.Chunks)
        {
            var raw = chunk.Blocks;
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != 0)
                {
                    cells.Add(CellPos.FromChunk(chunkPos, i));
                }
            }
        }

        return Compute(cells);
    }

    /// <summary>Computes the boxes of a set of cells.</summary>
    /// <param name="cells">Occupied cells.</param>
    /// <returns>The boxes.</returns>
    public static ShipHullBox[] Compute(IReadOnlyCollection<CellPos> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Count == 0)
        {
            return [];
        }

        for (var scale = 1; ; scale *= 2)
        {
            var boxes = Merge(cells, scale);
            if (boxes.Count <= MaxBoxes)
            {
                return [.. boxes];
            }
        }
    }

    private static List<ShipHullBox> Merge(IReadOnlyCollection<CellPos> cells, int scale)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var c in cells)
        {
            minX = Math.Min(minX, c.X);
            minY = Math.Min(minY, c.Y);
            minZ = Math.Min(minZ, c.Z);
            maxX = Math.Max(maxX, c.X);
            maxY = Math.Max(maxY, c.Y);
            maxZ = Math.Max(maxZ, c.Z);
        }

        int sx = ((maxX - minX) / scale) + 1, sy = ((maxY - minY) / scale) + 1, sz = ((maxZ - minZ) / scale) + 1;
        var grid = new bool[sx * sy * sz];
        foreach (var c in cells)
        {
            grid[Index((c.X - minX) / scale, (c.Y - minY) / scale, (c.Z - minZ) / scale, sx, sz)] = true;
        }

        var boxes = new List<ShipHullBox>();
        for (var y = 0; y < sy; y++)
        {
            for (var z = 0; z < sz; z++)
            {
                for (var x = 0; x < sx; x++)
                {
                    if (!grid[Index(x, y, z, sx, sz)])
                    {
                        continue;
                    }

                    var x1 = x + 1;
                    while (x1 < sx && grid[Index(x1, y, z, sx, sz)])
                    {
                        x1++;
                    }

                    var z1 = z + 1;
                    while (z1 < sz && RowFull(grid, x, x1, y, z1, sx, sz))
                    {
                        z1++;
                    }

                    var y1 = y + 1;
                    while (y1 < sy && LayerFull(grid, x, x1, y1, z, z1, sx, sz))
                    {
                        y1++;
                    }

                    for (var yy = y; yy < y1; yy++)
                    {
                        for (var zz = z; zz < z1; zz++)
                        {
                            for (var xx = x; xx < x1; xx++)
                            {
                                grid[Index(xx, yy, zz, sx, sz)] = false;
                            }
                        }
                    }

                    boxes.Add(new ShipHullBox(
                        new CellPos(minX + (x * scale), minY + (y * scale), minZ + (z * scale)),
                        new CellPos(minX + (x1 * scale), minY + (y1 * scale), minZ + (z1 * scale))));
                }
            }
        }

        return boxes;
    }

    private static bool RowFull(bool[] grid, int x0, int x1, int y, int z, int sx, int sz)
    {
        for (var x = x0; x < x1; x++)
        {
            if (!grid[Index(x, y, z, sx, sz)])
            {
                return false;
            }
        }

        return true;
    }

    private static bool LayerFull(bool[] grid, int x0, int x1, int y, int z0, int z1, int sx, int sz)
    {
        for (var z = z0; z < z1; z++)
        {
            if (!RowFull(grid, x0, x1, y, z, sx, sz))
            {
                return false;
            }
        }

        return true;
    }

    private static int Index(int x, int y, int z, int sx, int sz) => x + (sx * (z + (sz * y)));
}
