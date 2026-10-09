using System.Buffers.Binary;
using System.IO.Hashing;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Diagnostics;

/// <summary>A generator block to digest: block key at the given LOD (voxel origin = key * 16 * 2^lod).</summary>
public readonly record struct BlockSample(int Lod, int X, int Y, int Z)
{
    /// <summary>Edge of a block in voxels.</summary>
    public const int BlockSize = PlanetTerrainLayout.DataBlockSize;

    /// <summary>Voxel position of the block corner, in LOD 0 voxels.</summary>
    public (int X, int Y, int Z) Origin
    {
        get
        {
            var step = BlockSize << Lod;
            return (X * step, Y * step, Z * step);
        }
    }

    /// <summary>Order by (lod, x, y, z).</summary>
    public static IComparer<BlockSample> Ordering { get; } = Comparer<BlockSample>.Create(Compare);

    private static int Compare(BlockSample a, BlockSample b)
    {
        var c = a.Lod.CompareTo(b.Lod);
        if (c != 0)
        {
            return c;
        }

        c = a.X.CompareTo(b.X);
        if (c != 0)
        {
            return c;
        }

        c = a.Y.CompareTo(b.Y);
        return c != 0 ? c : a.Z.CompareTo(b.Z);
    }
}

/// <summary>
/// The deterministic set of blocks used by the tests and the compatibility report. Uses only arithmetic, <c>Sqrt</c> and integer
/// operations: <c>Math.Sin</c> and friends give different bits on different C libraries.
/// </summary>
public static class GeneratorSampler
{
    /// <summary>Number of face directions (6 cube faces times a 3 x 3 grid).</summary>
    public const int DirectionCount = 54;

    private const int ColumnMarginMeters = 330;
    private const int CaveBlockCount = 256;
    private const int CoreBlockCount = 64;
    private const int FarBlockCount = 64;

    /// <summary>Unit directions: integer points on the faces of a cube (coordinates -2, 0, 2 and 3 on the face axis), normalized.</summary>
    public static IReadOnlyList<(float X, float Y, float Z)> Directions()
    {
        var result = new List<(float, float, float)>(DirectionCount);
        ReadOnlySpan<int> grid = [-2, 0, 2];
        for (var axis = 0; axis < 3; axis++)
        {
            foreach (var sign in new[] { -3, 3 })
            {
                foreach (var u in grid)
                {
                    foreach (var v in grid)
                    {
                        var (x, y, z) = axis switch
                        {
                            0 => (sign, u, v),
                            1 => (u, sign, v),
                            _ => (u, v, sign),
                        };
                        result.Add(Normalize(x, y, z));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>LOD 0 block key containing the point at <paramref name="radius"/> along <paramref name="direction"/>.</summary>
    public static BlockSample LodZeroBlockAt((float X, float Y, float Z) direction, float radius) =>
        BlockContaining(0, MathF.Floor(direction.X * radius), MathF.Floor(direction.Y * radius), MathF.Floor(direction.Z * radius));

    /// <summary>All samples for a planet, sorted by (lod, x, y, z) without duplicates.</summary>
    public static IReadOnlyList<BlockSample> Create(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var radius = planet.RadiusMeters;
        var set = new SortedSet<BlockSample>(BlockSample.Ordering);

        foreach (var direction in Directions())
        {
            for (var t = radius - ColumnMarginMeters; t <= radius + ColumnMarginMeters; t += PlanetTerrainLayout.DataBlockSize)
            {
                set.Add(LodZeroBlockAt(direction, t));
            }
        }

        for (var i = 0; i < CaveBlockCount; i++)
        {
            var (direction, unit) = HashedDirection(1, i);
            set.Add(LodZeroBlockAt(direction, radius - 30f - (unit * 220f)));
        }

        for (var i = 0; i < CoreBlockCount; i++)
        {
            var (direction, unit) = HashedDirection(2, i);
            set.Add(LodZeroBlockAt(direction, radius - planet.CoreDepthMeters - 24f + (unit * 64f)));
        }

        for (var i = 0; i < FarBlockCount; i++)
        {
            var (direction, unit) = HashedDirection(3, i);
            var lod = 1 + (i % 9);
            var t = radius - 100f + (unit * 400f);
            set.Add(BlockContaining(lod, MathF.Floor(direction.X * t), MathF.Floor(direction.Y * t), MathF.Floor(direction.Z * t)));
        }

        return [.. set];
    }

    private static BlockSample BlockContaining(int lod, float x, float y, float z)
    {
        var step = PlanetTerrainLayout.DataBlockSize << lod;
        return new BlockSample(lod, (int)MathF.Floor(x / step), (int)MathF.Floor(y / step), (int)MathF.Floor(z / step));
    }

    private static (float X, float Y, float Z) Normalize(int x, int y, int z)
    {
        var length = MathF.Sqrt(((float)x * x) + ((float)y * y) + ((float)z * z));
        return (x / length, y / length, z / length);
    }

    /// <summary>Direction and a number in [0, 1) from the XxHash64 of (kind, index).</summary>
    private static ((float X, float Y, float Z) Direction, float Unit) HashedDirection(int kind, int index)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, kind);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], index);
        var hash = XxHash64.HashToUInt64(bytes);
        var x = (int)(hash & 0xFFFFF) - 0x80000;
        var y = (int)((hash >> 20) & 0xFFFFF) - 0x80000;
        var z = (int)((hash >> 40) & 0xFFFFF) - 0x80000;
        var unit = (XxHash64.HashToUInt64(bytes, 1) & 0xFFFF) / 65536f;
        if (x == 0 && y == 0 && z == 0)
        {
            x = 1;
        }

        var length = MathF.Sqrt(((float)x * x) + ((float)y * y) + ((float)z * z));
        return ((x / length, y / length, z / length), unit);
    }
}
