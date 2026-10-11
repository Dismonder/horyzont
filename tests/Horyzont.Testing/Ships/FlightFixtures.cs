using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World;

namespace Horyzont.Testing.Ships;

/// <summary>Ships and surroundings for flight tests (K5-24).</summary>
public static class FlightFixtures
{
    /// <summary>Gets the home planet of the default world.</summary>
    public static PlanetParams Home { get; } = WorldFactory.HomePlanetParams;

    /// <summary>A symmetric test ship: equal thrust toward every face, a pulse drive and landing gear.</summary>
    /// <param name="massKg">Mass.</param>
    /// <param name="thrustPerFace">Thrust toward every face, N.</param>
    /// <param name="fuelCapacity">Tank capacity.</param>
    /// <param name="upThrust">Thrust toward +Y when it differs, N.</param>
    /// <returns>The stats.</returns>
    public static ShipStats Cube(float massKg = 10_000f, float thrustPerFace = 200_000f, float fuelCapacity = 2000f, float? upThrust = null) =>
        new(
            massKg, new Vector3(1.5f, 1.5f, 1.5f), new ShipInertia(15_000f, 15_000f, 15_000f, 0, 0, 0),
            new ShipThrust(thrustPerFace, thrustPerFace, upThrust ?? thrustPerFace, thrustPerFace, thrustPerFace, thrustPerFace),
            60_000f, fuelCapacity, 27, 1, 6, 2, 2, 1, 4, []);

    /// <summary>The stats of <see cref="ShipFixtures.Thirty"/>.</summary>
    /// <returns>The stats.</returns>
    public static ShipStats Thirty()
    {
        var world = ShipFixtures.CreateWorld(ShipFixtures.Thirty);
        world.Constructs.TryGet(ShipFixtures.ConstructId, out var construct);
        return ShipStatsCalculator.Compute(construct!, ShipFixtures.ContentFor(world));
    }

    /// <summary>A motion above the home planet: upright, at rest, full fuel.</summary>
    /// <param name="altitude">Altitude above R + clearance.</param>
    /// <param name="fuel">Fuel units.</param>
    /// <param name="rules">Rules (clearance); default rules when null.</param>
    /// <returns>The motion.</returns>
    public static ShipMotion AboveHome(double altitude, float fuel = 2000f, FlightRules? rules = null) => new()
    {
        Pose = new PoseD(new Vector3D(0, Home.RadiusMeters + (rules ?? FlightRules.Default).SurfaceClearanceMeters + altitude, 0), QuaternionD.Identity),
        Fuel = fuel,
    };

    /// <summary>The environment of a motion in the home planet frame.</summary>
    /// <param name="motion">The motion.</param>
    /// <param name="rules">Rules; default when null.</param>
    /// <returns>The environment.</returns>
    public static FlightEnvironment HomeEnvironment(in ShipMotion motion, FlightRules? rules = null) =>
        FlightEnvironments.InPlanet(Home, 0, motion.Pose.Position, rules ?? FlightRules.Default);

    /// <summary>An environment without gravity, drag or bodies nearby.</summary>
    /// <returns>The environment.</returns>
    public static FlightEnvironment Void() => new()
    {
        Up = new Vector3D(0, 1, 0),
        Altitude = 1_000_000,
        RadiusOfBody = 1000,
        DistanceFromBody = 2_000_000,
        HorizontalSpeedCap = double.PositiveInfinity,
    };
}
