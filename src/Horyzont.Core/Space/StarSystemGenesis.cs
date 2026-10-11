using System.Buffers.Binary;
using System.IO.Hashing;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Space;

/// <summary>
/// Generation of the star system from the galaxy seed (K5-21 decision 3). Frozen like <see cref="PlanetGenesis"/>: the migration
/// M0002 backfills old worlds through it, so a different result is a new <see cref="StarSystemLayout"/>, never an edit.
/// </summary>
public static class StarSystemGenesis
{
    private const long FieldHashSeed = 3;
    private const uint RadiusField = 0;
    private const uint GravityField = 1;
    private const uint DistanceField = 2;
    private const uint AzimuthField = 3;
    private const uint ElevationField = 4;
    private const double UnitScale = 1.0 / (1UL << 53);

    /// <summary>
    /// Smallest distance of a body from the home planet that keeps their exit spheres apart:
    /// ⌈3.2 (R₀ + R) + 250⌉ + 1 m (the extra metre covers rounding the position to whole metres).
    /// </summary>
    /// <param name="homeRadius">Radius of the home planet.</param>
    /// <param name="bodyRadius">Radius of the body.</param>
    /// <returns>The distance in metres.</returns>
    public static double MinimumDistanceFromHome(double homeRadius, double bodyRadius) =>
        Math.Ceiling((StarSystemRules.ZoneExitFactor * (homeRadius + bodyRadius)) + StarSystemRules.MinZoneGapMeters) + 1;

    /// <summary>Creates the bodies of a new world: the home planet of <see cref="PlanetGenesis"/> and the system of <see cref="StarSystemLayout.V1"/>.</summary>
    /// <param name="galaxySeed">Galaxy seed.</param>
    /// <param name="generatorProfile">Generator profile of the home planet (used by every body).</param>
    /// <returns>The system and its planets.</returns>
    public static GeneratedStarSystem CreateWorldBodies(ulong galaxySeed, string generatorProfile) =>
        Create(galaxySeed, PlanetGenesis.CreatePlanet(galaxySeed, PlanetGenesis.HomePlanetIndex, generatorProfile), StarSystemLayout.V1);

    /// <summary>Generates a star system around an existing home planet.</summary>
    /// <param name="galaxySeed">Galaxy seed.</param>
    /// <param name="home">The home planet; becomes body 0 at the origin.</param>
    /// <param name="layout">The frozen layout version.</param>
    /// <returns>The system and its planets in body order (element 0 is <paramref name="home"/>).</returns>
    /// <exception cref="InvalidDataException">A planet id collides or the zones would overlap (never for <see cref="StarSystemLayout.V1"/> with the default home planet).</exception>
    public static GeneratedStarSystem Create(ulong galaxySeed, PlanetParams home, StarSystemLayout layout)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(layout);
        home.Validate();

        var count = layout.Bodies.Count + 1;
        var planets = new PlanetParams[count];
        var placements = new BodyPlacement[count];
        planets[0] = home;
        placements[0] = new BodyPlacement(home.Id, 0, PoseD.Identity);
        var maxElevation = layout.MaxElevationDegrees * Math.PI / 180;

        for (var k = 1; k < count; k++)
        {
            var body = layout.Bodies[k - 1];
            var index = (uint)k;
            var radius = body.MinRadiusMeters + (10 * Pick(galaxySeed, index, RadiusField, ((body.MaxRadiusMeters - body.MinRadiusMeters) / 10) + 1));
            var gravityCentis = body.MinGravityCentis + Pick(galaxySeed, index, GravityField, body.MaxGravityCentis - body.MinGravityCentis + 1);
            var distance = (double)(body.MinDistanceMeters + Pick(galaxySeed, index, DistanceField, body.MaxDistanceMeters - body.MinDistanceMeters + 1));
            distance = Math.Max(distance, MinimumDistanceFromHome(home.RadiusMeters, radius));
            var azimuth = 2 * Math.PI * Unit(galaxySeed, index, AzimuthField);
            var elevation = ((2 * Unit(galaxySeed, index, ElevationField)) - 1) * maxElevation;

            var flat = distance * Math.Cos(elevation);
            var position = new Vector3D(
                Math.Round(flat * Math.Cos(azimuth)),
                Math.Round(distance * Math.Sin(elevation)),
                Math.Round(flat * Math.Sin(azimuth)));

            var planet = new PlanetParams(
                PlanetGenesis.DerivePlanetId(galaxySeed, index),
                new SystemId(StarSystemRules.SystemIdValue),
                PlanetGenesis.DerivePlanetSeed(galaxySeed, index),
                home.GeneratorProfile,
                RadiusMeters: radius,
                VoxelSizeMeters: home.VoxelSizeMeters,
                Gravity: gravityCentis / 100f,
                AtmosphereHeightMeters: body.AtmosphereHeightMeters,
                CoreDepthMeters: body.CoreDepthMeters,
                DayLengthSeconds: home.DayLengthSeconds);
            planet.Validate();
            for (var j = 0; j < k; j++)
            {
                if (planets[j].Id == planet.Id)
                {
                    throw new InvalidDataException($"Body {k} of galaxy {galaxySeed} collides with planet {planet.Id.Value}.");
                }
            }

            planets[k] = planet;
            placements[k] = new BodyPlacement(planet.Id, (byte)k, new PoseD(position, QuaternionD.Identity));
        }

        var system = new StarSystemParams(new SystemId(StarSystemRules.SystemIdValue), layout.Version, home.Id, home.DayLengthSeconds, placements);
        system.Validate();
        ValidateSeparation(system, planets);
        return new GeneratedStarSystem(system, planets);
    }

    /// <summary>Checks that the exit spheres (3.2 R) of every pair of bodies are at least <see cref="StarSystemRules.MinZoneGapMeters"/> apart.</summary>
    /// <param name="system">The system.</param>
    /// <param name="planets">Planets in body order.</param>
    /// <exception cref="InvalidDataException">Two zones are too close.</exception>
    internal static void ValidateSeparation(StarSystemParams system, IReadOnlyList<PlanetParams> planets)
    {
        for (var a = 0; a < system.Bodies.Count; a++)
        {
            for (var b = a + 1; b < system.Bodies.Count; b++)
            {
                var distance = Vector3D.Distance(system.Bodies[a].Pose.Position, system.Bodies[b].Pose.Position);
                var required = (StarSystemRules.ZoneExitFactor * ((double)planets[a].RadiusMeters + planets[b].RadiusMeters))
                    + StarSystemRules.MinZoneGapMeters;
                if (distance < required)
                {
                    throw new InvalidDataException(
                        $"Star system {system.System.Value}: bodies {system.Bodies[a].BodyIndex} and {system.Bodies[b].BodyIndex} are {distance} m apart, {required} m required.");
                }
            }
        }
    }

    private static int Pick(ulong galaxySeed, uint body, uint field, int choices) =>
        (int)(Hash(galaxySeed, body, field) % (ulong)choices);

    private static double Unit(ulong galaxySeed, uint body, uint field) => (Hash(galaxySeed, body, field) >> 11) * UnitScale;

    private static ulong Hash(ulong galaxySeed, uint body, uint field)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, galaxySeed);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], body);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], field);
        return XxHash64.HashToUInt64(bytes, FieldHashSeed);
    }
}
