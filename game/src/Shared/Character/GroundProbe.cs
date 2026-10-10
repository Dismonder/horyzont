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
/// physics ray straight down the planet "up"; the caller chooses terrain readiness or combined terrain/construct support.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The ray query is a reference-counted Godot object that the engine frees with the last reference; the probe lives as long as its character.")]
public sealed class GroundProbe
{
    /// <summary>How far above the feet the ray starts (metres), so feet slightly inside the surface still find it.</summary>
    public const float StartAbove = 1f;

    private readonly PlanetId _planet;
    private readonly uint _mask;
    private PhysicsRayQueryParameters3D? _query;

    /// <summary>Initializes a new instance of the <see cref="GroundProbe"/> class.</summary>
    /// <param name="planet">The planet whose frame positions are in.</param>
    /// <param name="mask">Collision layers to search; terrain only by default for existing terrain readiness callers.</param>
    public GroundProbe(PlanetId planet, uint mask = CollisionLayers.TerrainMask)
    {
        _planet = planet;
        _mask = mask;
    }

    /// <summary>Looks for terrain collision below a position.</summary>
    /// <param name="world">The physics world to ask.</param>
    /// <param name="feet">Position in the planet frame.</param>
    /// <param name="below">How far below the feet to look (metres).</param>
    /// <param name="ground">The collision point when found.</param>
    /// <param name="startAbove">Height above the feet where the support search begins.</param>
    /// <returns>Whether collision exists within the range.</returns>
    public bool TryFindGround(World3D world, NumVector3 feet, float below, out NumVector3 ground, float startAbove = StartAbove)
    {
        ArgumentNullException.ThrowIfNull(world);
        var up = TerrainMath.UpAt(feet);

        // One query object per probe; the dictionary IntersectRay returns is allocated by the engine API and cannot be avoided.
        var query = _query ??= new PhysicsRayQueryParameters3D { CollisionMask = _mask };
        query.From = PlanetFrames.ToGodot(_planet, feet + (up * startAbove));
        query.To = PlanetFrames.ToGodot(_planet, feet - (up * below));
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
