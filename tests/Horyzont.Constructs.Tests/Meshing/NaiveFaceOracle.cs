using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Tests.Meshing;

/// <summary>Straightforward per-cell, per-face computation of the exposed cube faces, used as the oracle of the greedy mesher.</summary>
internal static class NaiveFaceOracle
{
    private sealed record CellInfo(bool IsCube, bool IsSolid, int Material);

    /// <summary>Exposed render faces (cube next to a non-cube) with their material, and exposed collision faces.</summary>
    public static (Dictionary<(int, int, int, Face), int> Render, HashSet<(int, int, int, Face)> Collision) Faces(ReadOnlySpan<uint> cells)
    {
        var render = new Dictionary<(int, int, int, Face), int>();
        var collision = new HashSet<(int, int, int, Face)>();
        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < 16; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    var self = Info(cells[Horyzont.Constructs.Meshing.ChunkMeshInput.Index(x, y, z)]);
                    for (var f = 0; f < 6; f++)
                    {
                        var face = (Face)f;
                        var (nx, ny, nz) = Neighbour(x, y, z, face);
                        var other = Info(cells[Horyzont.Constructs.Meshing.ChunkMeshInput.Index(nx, ny, nz)]);
                        if (self.IsCube && !other.IsCube)
                        {
                            render[(x, y, z, face)] = self.Material;
                        }

                        if (self.IsSolid && !other.IsSolid)
                        {
                            collision.Add((x, y, z, face));
                        }
                    }
                }
            }
        }

        return (render, collision);
    }

    private static (int, int, int) Neighbour(int x, int y, int z, Face face) => face switch
    {
        Face.PosX => (x + 1, y, z),
        Face.NegX => (x - 1, y, z),
        Face.PosY => (x, y + 1, z),
        Face.NegY => (x, y - 1, z),
        Face.PosZ => (x, y, z + 1),
        _ => (x, y, z - 1),
    };

    private static CellInfo Info(uint raw)
    {
        if (raw == 0)
        {
            return new CellInfo(false, false, 0);
        }

        var state = new BlockState(raw);
        if (!state.IsWellFormed)
        {
            return new CellInfo(true, true, 1);
        }

        return state.TypeId switch
        {
            TestContent.Stone => new CellInfo(true, true, 2),
            TestContent.Wood => new CellInfo(true, true, 3),
            TestContent.Ghost => new CellInfo(true, false, 3),
            TestContent.Slab or TestContent.Slope => new CellInfo(false, false, 0),
            _ => new CellInfo(true, true, 1), // core:missing and ids unknown to the registry
        };
    }
}
