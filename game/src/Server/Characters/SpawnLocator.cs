using Horyzont.Core.Motion;
using Horyzont.Core.World;
using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Generators;

using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Server.Characters;

/// <summary>
/// Where a character starts: the start direction (+X, the equator; owner question P5/P6) at the surface height the generator of the
/// planet gives, plus a margin. The terrain collision does not exist yet when the player joins, so the character is created
/// frozen and the ground wait of <see cref="ServerCharacters"/> puts it on the collision once it is built. Never throws.
/// </summary>
public static class SpawnLocator
{
    /// <summary>The start direction of a new player on the planet.</summary>
    public static readonly Vector3 StartDirection = Vector3.UnitX;

    /// <summary>Metres above the generated surface where a spawned character appears.</summary>
    public const float SpawnClearance = 2f;

    /// <summary>The pose of a player who joins for the first time.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The feet position in the planet frame and the body rotation facing "north".</returns>
    public static (Vector3 Feet, Quaternion BodyRotation) ForNewPlayer(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return (AboveSurface(planet, StartDirection), SurfaceFrame.FromYaw(StartDirection, 0f));
    }

    /// <summary>A point above the surface in a direction from the centre: used for the start and for the rescue from below the core.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="direction">Direction from the planet centre; any non-zero vector (zero falls back to the start direction).</param>
    /// <returns>The feet position, <see cref="SpawnClearance"/> above the generated surface.</returns>
    public static Vector3 AboveSurface(PlanetParams planet, Vector3 direction)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var length = direction.Length();
        var unit = float.IsFinite(length) && length > 1e-6f ? direction / length : StartDirection;
        return unit * (SurfaceRadius(planet, unit) + SpawnClearance);
    }

    /// <summary>Radius of the highest solid voxel in a direction, found with the generator of the planet; the highest possible relief when it cannot be probed.</summary>
    private static float SurfaceRadius(PlanetParams planet, Vector3 unit)
    {
        var highest = planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters;
        try
        {
            using var generator = GeneratorProfiles.CreateGenerator(planet);
            using var probe = new GeneratorProbe(generator);
            var lowest = Math.Max(planet.RadiusMeters - planet.CoreDepthMeters, planet.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - 16f);
            for (var radius = highest + 16f; radius >= lowest; radius -= 1f)
            {
                var point = unit * radius;
                if (probe.Sample((int)MathF.Round(point.X), (int)MathF.Round(point.Y), (int)MathF.Round(point.Z)).Sdf < 0f)
                {
                    return radius;
                }
            }
        }
        catch (NotSupportedException)
        {
            // A profile this build cannot generate: the highest possible surface is a safe start height; the ground wait does the rest.
        }

        return highest;
    }
}
