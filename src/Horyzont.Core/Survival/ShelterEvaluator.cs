using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Survival;

/// <summary>The shelter around a point: 26 rays of <see cref="HazardRules.ShelterRayMeters"/> (K6-34, D11).</summary>
public static class ShelterEvaluator
{
    /// <summary>
    /// Casts the 26 rays of <see cref="ShelterDirections"/> from <paramref name="origin"/> in the frame of <paramref name="planet"/>; a ray hits when it meets an occupied block of a construct of that planet
    /// or the terrain within the length of the ray (inclusive). The up direction is the one of the planet at the origin, never an axis of a construct.
    /// </summary>
    /// <remarks>
    /// The rays go through <see cref="ITerrainService.Raycast"/> (never a sampling of the solid), and the constructs through <see cref="ConstructRaycast"/>, which looks at the constructs of the frame of the planet
    /// only, so a construct of another planet is ignored, and skips the ones whose bounding sphere the ray cannot reach. The terrain of an area that is not loaded on the server misses, and the player then
    /// leaves the shelter: callers that need a stable answer evaluate where the terrain is loaded (the hazard tracker of the server, like the rest tracker of K6-33). Does not allocate.
    /// </remarks>
    /// <param name="state">The world (the constructs).</param>
    /// <param name="terrain">The terrain; main thread only.</param>
    /// <param name="planet">The planet of the origin.</param>
    /// <param name="origin">The origin in the frame of the planet (<see cref="TryGetOrigin"/>).</param>
    /// <param name="rules">The numbers of the rays.</param>
    /// <returns>The hits, whether the ray up hit and whether that is a shelter.</returns>
    public static ShelterResult Evaluate(WorldState state, ITerrainService terrain, PlanetId planet, Vector3 origin, HazardRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(rules);
        var frame = FrameRef.Of(planet);
        ShelterDirections.Basis(TerrainMath.UpAt(origin), out var x, out var y, out var z);
        var hits = 0;
        var upHit = false;
        for (var i = 0; i < ShelterDirections.Count; i++)
        {
            var local = ShelterDirections.Local(i);
            var direction = (x * local.X) + (y * local.Y) + (z * local.Z);
            if (ConstructRaycast.Raycast(state.Constructs, frame, origin, direction, rules.ShelterRayMeters, out _)
                || terrain.Raycast(planet, origin, direction, rules.ShelterRayMeters, out _))
            {
                hits++;
                upHit |= i == ShelterDirections.UpIndex;
            }
        }

        return new ShelterResult((byte)hits, upHit, upHit && hits >= rules.ShelterRequiredHits);
    }

    /// <summary>
    /// The origin of the rays and of the generator distance for a player (D11, D12): the feet in the frame of the planet plus the up of the planet times
    /// <see cref="HazardRules.ShelterOriginHeightMeters"/>, through the same frame transform as the eye of <c>ContainerReach</c>.
    /// </summary>
    /// <param name="state">The world.</param>
    /// <param name="player">The player.</param>
    /// <param name="rules">The numbers of the rays.</param>
    /// <param name="planet">The planet the player stands on.</param>
    /// <param name="origin">The origin in the frame of that planet.</param>
    /// <returns>False when the player is not on a planet (e.g. in the frame of the star system).</returns>
    public static bool TryGetOrigin(WorldState state, PlayerState player, HazardRules rules, out PlanetId planet, out Vector3 origin)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(rules);
        if (!state.TryToPlanetFrame(player.Frame, player.Position, out planet, out var feet))
        {
            origin = default;
            return false;
        }

        origin = feet + (TerrainMath.UpAt(feet) * rules.ShelterOriginHeightMeters);
        return true;
    }
}
