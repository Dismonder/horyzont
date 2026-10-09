using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Testing;

/// <summary>Block patterns of a 64³ construct (4³ chunks) for meshing and frame-time tests.</summary>
public enum ConstructPattern
{
    /// <summary>Every cell is stone: the smallest mesh, the largest collision-free interior.</summary>
    Solid,

    /// <summary>A building shell with floors every 4 m, 2×2 windows every 6 m and partition walls; mixed types incl. slabs and slopes.</summary>
    Building,

    /// <summary>35 % of the cells filled at random (seed 12).</summary>
    Random35,

    /// <summary>3D checkerboard: the worst case for face counts (report only).</summary>
    Checkerboard,
}

/// <summary>Constructs of 64³ cells built through the public <see cref="WorldStateBuilder"/>, the way a checkpoint loads.</summary>
public static class ConstructFixtures
{
    /// <summary>Edge length in cells.</summary>
    public const int Size = 64;

    /// <summary>Entity id of the fixture construct.</summary>
    public static readonly EntityId ConstructId = new(1);

    /// <summary>Block type keys in registry order (ids 1 … 5; core:missing is 6).</summary>
    public static readonly string[] BlockKeys =
    [
        "core:stone_block", "core:wood_planks", "core:metal_plate", "core:stone_slab", "core:stone_slope",
    ];

    private const ushort Stone = 1;
    private const ushort Planks = 2;
    private const ushort Plate = 3;
    private const ushort Slab = 4;
    private const ushort Slope = 5;

    private static readonly Vector3 _origin = new(0, 4000, 0);

    /// <summary>A world holding one 64³ construct with the pattern; the registry holds <see cref="BlockKeys"/> and core:missing.</summary>
    /// <param name="pattern">Block pattern.</param>
    /// <param name="cubesOnly">Replaces slabs and slopes by stone cubes (Core treats every block as a full cube).</param>
    /// <returns>The loaded world.</returns>
    public static WorldState Create(ConstructPattern pattern, bool cubesOnly = false)
    {
        var builder = new WorldStateBuilder(WorldFactory.DefaultWorldId, 42);
        builder.SetProgress(default, default, 2, 1);
        builder.AddPlanet(WorldFactory.HomePlanetParams);
        for (var i = 0; i < BlockKeys.Length; i++)
        {
            builder.AddBlockType(new RegistryEntry((ushort)(i + 1), BlockKeys[i]));
        }

        builder.AddBlockType(new RegistryEntry((ushort)(BlockKeys.Length + 1), BlockTypeRegistry.MissingKey));
        builder.AddMaterial(new RegistryEntry(1, "stone"));
        builder.AddMaterial(new RegistryEntry(2, "dirt"));
        builder.AddConstruct(new ConstructHeaderRecord(
            ConstructId, FrameRef.Of(WorldFactory.HomePlanet), 0, ConstructMode.Static, _origin, Quaternion.Identity, 1));
        for (var cx = 0; cx < Size / 16; cx++)
        {
            for (var cy = 0; cy < Size / 16; cy++)
            {
                for (var cz = 0; cz < Size / 16; cz++)
                {
                    var blocks = new uint[ConstructChunk.CellCount];
                    var any = false;
                    for (var i = 0; i < blocks.Length; i++)
                    {
                        var cell = CellPos.FromChunk(new ChunkPos(cx, cy, cz), i);
                        blocks[i] = Cell(pattern, cell.X, cell.Y, cell.Z);
                        if (cubesOnly && new BlockState(blocks[i]).TypeId is Slab or Slope)
                        {
                            blocks[i] = Pack(Stone, 0);
                        }

                        any |= blocks[i] != 0;
                    }

                    if (any)
                    {
                        builder.AddChunk(new ConstructChunkRecord(new ConstructChunkKey(ConstructId, new ChunkPos(cx, cy, cz)), blocks));
                    }
                }
            }
        }

        return builder.Build();
    }

    /// <summary>Packed block of a cell (0 = empty) for the cells 0 … 63 on each axis.</summary>
    /// <param name="pattern">Block pattern.</param>
    /// <param name="x">Cell X.</param>
    /// <param name="y">Cell Y.</param>
    /// <param name="z">Cell Z.</param>
    /// <returns>The packed <see cref="BlockState"/>.</returns>
    public static uint Cell(ConstructPattern pattern, int x, int y, int z) => pattern switch
    {
        ConstructPattern.Solid => Pack(Stone, 0),
        ConstructPattern.Checkerboard => (x + y + z) % 2 == 0 ? Pack(Stone, 0) : 0,
        ConstructPattern.Random35 => RandomCell(x, y, z),
        _ => BuildingCell(x, y, z),
    };

    private static uint Pack(ushort type, byte orientation) => BlockState.Create(type, orientation).Packed;

    private static uint RandomCell(int x, int y, int z)
    {
        var h = Mix(12UL, (ulong)(((x * Size) + y) * Size) + (ulong)z);
        if (h % 100 >= 35)
        {
            return 0;
        }

        return Pack((ushort)(1 + (int)((h >> 16) % 3)), 0);
    }

    private static uint BuildingCell(int x, int y, int z)
    {
        var onWall = x == 0 || x == Size - 1 || z == 0 || z == Size - 1;
        var horizontal = x == 0 || x == Size - 1 ? z : x;

        // Floors and the roof every 4 m.
        if (y % 4 == 0 || y == Size - 1)
        {
            return Pack(Planks, 0);
        }

        if (onWall)
        {
            // 2x2 windows every 6 m; a slab sill below and a slope lintel-cap above the window rows.
            var windowColumn = horizontal % 6 is 2 or 3;
            var windowRow = y % 6 is 2 or 3;
            if (windowColumn && windowRow)
            {
                return 0;
            }

            if (windowColumn && y % 6 == 1)
            {
                return Pack(Slab, (byte)((x + z) % 24));
            }

            if (windowColumn && y % 6 == 4)
            {
                return Pack(Slope, (byte)((horizontal + y) % 24));
            }

            return Pack(Stone, 0);
        }

        // Partition walls every 16 m with a doorway, in metal plate.
        if ((x % 16 == 8 || z % 16 == 8) && (x % 16 == 8 ? z : x) % 16 is < 6 or > 8)
        {
            return Pack(Plate, 0);
        }

        return 0;
    }

    private static ulong Mix(ulong seed, ulong value)
    {
        var z = seed + (value * 0x9E3779B97F4A7C15UL) + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
