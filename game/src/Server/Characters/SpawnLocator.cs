using Horyzont.Core.Motion;
using Horyzont.Core.World;
using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Generators;

using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Server.Characters;

/// <summary>
/// Where a character starts: the start direction (+X, the equator; owner question P5/P6) at the surface height the generator of the
/// planet gives, plus a margin. A new player starts on the nearest gentle slope around that direction (the relief of the profile has
/// cliffs, and a character that spawns on one slides off it). The terrain collision does not exist yet when the player joins, so the
/// character is created frozen and the ground wait of <see cref="ServerCharacters"/> puts it on the collision once it is built.
/// Never throws.
/// </summary>
public static class SpawnLocator
{
    /// <summary>Metres above the generated surface where a spawned character appears.</summary>
    public const float SpawnClearance = 2f;

    /// <summary>Largest slope (rise over run) of a start site; about 15 degrees, well below the 46 degrees a body can stand on.</summary>
    public const float MaxStartSlope = 0.27f;

    /// <summary>Distance between the candidate start sites in metres.</summary>
    public const float CandidateSpacingMeters = 24f;

    /// <summary>How many candidate rings around the start direction are tried before the plain start direction is used.</summary>
    public const int CandidateRings = 8;

    private const float CoarseStepMeters = 4f;
    private const float SlopeSampleMeters = 3f;

    /// <summary>The start direction of a new player on the planet.</summary>
    public static readonly Vector3 StartDirection = Vector3.UnitX;

    /// <summary>The pose of a player who joins for the first time.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The feet position in the planet frame and the body rotation facing "north".</returns>
    public static (Vector3 Feet, Quaternion BodyRotation) ForNewPlayer(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        try
        {
            using var generator = GeneratorProfiles.CreateGenerator(planet);
            using var probe = new GeneratorProbe(generator);
            foreach (var candidate in Candidates(planet))
            {
                if (TrySite(planet, probe, candidate, out var height, out var slope) && slope <= MaxStartSlope)
                {
                    return Pose(candidate, height);
                }
            }

            if (TryScan(planet, probe, StartDirection, out var plain))
            {
                return Pose(StartDirection, plain);
            }
        }
        catch (NotSupportedException)
        {
            // A profile this build cannot generate: the highest possible surface in the start direction is the answer.
        }

        return Pose(StartDirection, planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters);
    }

    /// <summary>A point above the surface in a direction from the centre: used for the rescue from below the core.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="direction">Direction from the planet centre; any non-zero vector (zero falls back to the start direction).</param>
    /// <returns>The feet position, <see cref="SpawnClearance"/> above the generated surface.</returns>
    public static Vector3 AboveSurface(PlanetParams planet, Vector3 direction)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var length = direction.Length();
        var unit = float.IsFinite(length) && length > 1e-6f ? direction / length : StartDirection;
        return unit * (SurfaceRadiusOrHighest(planet, unit) + SpawnClearance);
    }

    private static (Vector3 Feet, Quaternion BodyRotation) Pose(Vector3 direction, float surfaceRadius) =>
        (direction * (surfaceRadius + SpawnClearance), SurfaceFrame.FromYaw(direction, 0f));

    private static IEnumerable<Vector3> Candidates(PlanetParams planet)
    {
        var step = CandidateSpacingMeters / planet.RadiusMeters;
        var cells = new List<(int East, int North)>();
        for (var east = -CandidateRings; east <= CandidateRings; east++)
        {
            for (var north = -CandidateRings; north <= CandidateRings; north++)
            {
                cells.Add((east, north));
            }
        }

        cells.Sort((a, b) =>
        {
            var byRing = ((a.East * a.East) + (a.North * a.North)).CompareTo((b.East * b.East) + (b.North * b.North));
            return byRing != 0 ? byRing : (a.East, a.North).CompareTo((b.East, b.North));
        });
        foreach (var (east, north) in cells)
        {
            yield return Vector3.Normalize(StartDirection + (Vector3.UnitZ * (east * step)) + (Vector3.UnitY * (north * step)));
        }
    }

    private static bool TrySite(PlanetParams planet, GeneratorProbe probe, Vector3 unit, out float height, out float slope)
    {
        height = float.NaN;
        slope = float.PositiveInfinity;
        if (!TryScan(planet, probe, unit, out height))
        {
            return false;
        }

        // The slope from the surface radius a few metres away on two tangent axes.
        var point = unit * height;
        var around = Vector3.Cross(unit, Vector3.UnitY);
        var tangentA = Vector3.Normalize(around.LengthSquared() > 1e-6f ? around : Vector3.UnitZ);
        var tangentB = Vector3.Normalize(Vector3.Cross(unit, tangentA));
        var worst = 0f;
        foreach (var tangent in new[] { tangentA, -tangentA, tangentB, -tangentB })
        {
            var neighbour = Vector3.Normalize(point + (tangent * SlopeSampleMeters));
            if (!TryScan(planet, probe, neighbour, out var neighbourHeight))
            {
                return false;
            }

            worst = MathF.Max(worst, MathF.Abs(neighbourHeight - height) / SlopeSampleMeters);
        }

        slope = worst;
        return true;
    }

    private static float SurfaceRadiusOrHighest(PlanetParams planet, Vector3 unit)
    {
        try
        {
            using var generator = GeneratorProfiles.CreateGenerator(planet);
            using var probe = new GeneratorProbe(generator);
            if (TryScan(planet, probe, unit, out var radius))
            {
                return radius;
            }
        }
        catch (NotSupportedException)
        {
            // A profile this build cannot generate: the highest possible surface is a safe start height; the ground wait does the rest.
        }

        return planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters;
    }

    /// <summary>Radius of the highest solid voxel along a direction: a coarse scan from above, then a fine one.</summary>
    private static bool TryScan(PlanetParams planet, GeneratorProbe probe, Vector3 unit, out float radius)
    {
        var highest = planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + 16f;
        var lowest = MathF.Max(planet.RadiusMeters - planet.CoreDepthMeters, planet.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - 16f);
        for (var coarse = highest; coarse >= lowest; coarse -= CoarseStepMeters)
        {
            if (!IsSolid(probe, unit * coarse))
            {
                continue;
            }

            for (var fine = coarse + CoarseStepMeters; fine >= coarse; fine -= 1f)
            {
                if (IsSolid(probe, unit * fine))
                {
                    radius = fine;
                    return true;
                }
            }

            radius = coarse;
            return true;
        }

        radius = float.NaN;
        return false;
    }

    private static bool IsSolid(GeneratorProbe probe, Vector3 point) =>
        probe.Sample((int)MathF.Round(point.X), (int)MathF.Round(point.Y), (int)MathF.Round(point.Z)).Sdf < 0f;
}
