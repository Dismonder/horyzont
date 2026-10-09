using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Character;

/// <summary>
/// Tells whether the terrain collision under a position already exists: Voxel Tools builds collision shapes asynchronously after
/// the blocks load, so a body placed before that would fall through the planet (K2-11, "spawn before collisions"). The probe is a
/// physics ray straight down the planet "up", on the terrain layer only.
/// </summary>
public sealed class GroundProbe
{
    /// <summary>How far above the feet the ray starts (metres), so feet slightly inside the surface still find it.</summary>
    public const float StartAbove = 1f;

    private readonly PlanetId _planet;

    /// <summary>Initializes a new instance of the <see cref="GroundProbe"/> class.</summary>
    /// <param name="planet">The planet whose frame positions are in.</param>
    public GroundProbe(PlanetId planet) => _planet = planet;

    /// <summary>Looks for terrain collision below a position.</summary>
    /// <param name="world">The physics world to ask.</param>
    /// <param name="feet">Position in the planet frame.</param>
    /// <param name="below">How far below the feet to look (metres).</param>
    /// <param name="ground">The collision point when found.</param>
    /// <returns>Whether collision exists within the range.</returns>
    public bool TryFindGround(World3D world, NumVector3 feet, float below, out NumVector3 ground)
    {
        ArgumentNullException.ThrowIfNull(world);
        var up = TerrainMath.UpAt(feet);
        using var query = PhysicsRayQueryParameters3D.Create(
            PlanetFrames.ToGodot(_planet, feet + (up * StartAbove)), PlanetFrames.ToGodot(_planet, feet - (up * below)), CollisionLayers.TerrainMask);
        var hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            ground = default;
            return false;
        }

        ground = PlanetFrames.ToPlanetLocal(_planet, hit["position"].AsVector3());
        return true;
    }
}
