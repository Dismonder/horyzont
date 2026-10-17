using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// The reference scenes of the shelter tests (K6-34 D11): a hut of 5 x 5 x 5 cells (walls one cell thick, interior 3 x 3 x 3, the player's origin in the middle of the interior) on the fake terrain, a sphere of
/// radius 4000 m around the planet centre. The construct stands with the corner of its cell (0,0,0) at (0, 4000, 0) of the home planet; at the axis of the planet the local up of the terrain is the world Y, so the scenes
/// are exact. In the open field the nine rays that point down hit the terrain, the eight horizontal ones and the nine pointing up miss, so the numbers of hits of the scenes are 9 in the open, 18 under a roof alone,
/// 25 inside walls without a roof or a hut with one opening, and 26 in a closed hut.
/// </summary>
internal static class ShelterScenes
{
    /// <summary>The construct of the hut.</summary>
    public static readonly EntityId HutId = new(200);

    /// <summary>The corner of cell (0,0,0) of the hut in the frame of the planet.</summary>
    public static readonly Vector3 Corner = new(0f, 4000f, 0f);

    /// <summary>The middle of the interior of the hut in the construct frame.</summary>
    public static readonly Vector3 LocalCenter = new(2.5f, 2.5f, 2.5f);

    /// <summary>The origin of the rays in the middle of the interior of a hut without rotation.</summary>
    public static Vector3 Center => Corner + LocalCenter;

    /// <summary>The hut shell: a cube of 5 cells whose outer layer is solid.</summary>
    public static bool Closed(int x, int y, int z) => x is 0 or 4 || y is 0 or 4 || z is 0 or 4;

    /// <summary>The hut with a doorway: the middle cell of the +X wall is missing, so exactly one ray (along +X) escapes.</summary>
    public static bool WithDoorway(int x, int y, int z) => Closed(x, y, z) && !(x == 4 && y == 2 && z == 2);

    /// <summary>The hut with a skylight: the middle cell of the roof is missing, so exactly the ray straight up escapes.</summary>
    public static bool WithSkylight(int x, int y, int z) => Closed(x, y, z) && !(x == 2 && y == 4 && z == 2);

    /// <summary>A roof alone: the layer y = 4.</summary>
    public static bool RoofOnly(int x, int y, int z) => y == 4;

    /// <summary>High walls without a roof: the four side walls up to y = 12.</summary>
    public static bool TallWallsNoRoof(int x, int y, int z) => x is 0 or 4 || z is 0 or 4;

    /// <summary>Builds a world with one construct whose cells are those of <paramref name="wall"/> inside 5 x <paramref name="height"/> x 5.</summary>
    /// <param name="wall">Whether the cell is a block.</param>
    /// <param name="rotation">Rotation of the construct to the frame of the planet; null for none.</param>
    /// <param name="ship">Whether the construct is a ship (a parked ship; the evaluation does not care).</param>
    /// <param name="height">Number of layers.</param>
    /// <param name="secondPlanet">Whether the construct stands on the second planet instead of the home planet.</param>
    /// <returns>The simulation fixture; its terrain is the sphere of 4000 m.</returns>
    public static SimFixture Scene(Func<int, int, int, bool> wall, Quaternion? rotation = null, bool ship = false, int height = 5, bool secondPlanet = false)
    {
        var f = new SimFixture(state: WorldFactory.CreateWith(WorldFactory.HomePlanetParams, WorldFactory.SecondPlanetParams));
        var cells = new List<CellPos>();
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < height; y++)
            {
                for (var z = 0; z < 5; z++)
                {
                    if (wall(x, y, z))
                    {
                        cells.Add(new CellPos(x, y, z));
                    }
                }
            }
        }

        var parent = FrameRef.Of(secondPlanet ? WorldFactory.SecondPlanet : WorldFactory.HomePlanet);
        f.Sim.ApplyServerOperation(new ConstructCreateOp(HutId, parent, Corner, rotation ?? Quaternion.Identity, cells[0], BlockState.Create(f.Stone, 0)));
        foreach (var cell in cells.Skip(1))
        {
            f.SetBlock(HutId, cell);
        }

        if (ship)
        {
            var construct = f.State.Constructs.All.Single(c => c.Id == HutId);
            f.Sim.ApplyServerOperation(new ShipModeSetOp(HutId, ConstructMode.Ship, construct.Parent, construct.ParentSeq, construct.Origin, construct.Rotation));
        }

        return f;
    }

    /// <summary>The middle of the interior of a hut turned by <paramref name="rotation"/> about its corner, in the frame of the planet.</summary>
    /// <param name="rotation">The rotation of the construct.</param>
    /// <returns>The point.</returns>
    public static Vector3 CenterOf(Quaternion rotation) => Corner + Vector3.Transform(LocalCenter, rotation);

    /// <summary>A rotation about the Z axis.</summary>
    /// <param name="degrees">The angle.</param>
    /// <returns>The rotation.</returns>
    public static Quaternion AboutZ(float degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees * MathF.PI / 180f);
}
