using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;
using Horyzont.Core.World;

namespace Horyzont.Client.Ships;

/// <summary>
/// The flight environment of the predicted ship (K5-25 decision 16), built exactly like <c>ShipFlightSystem.Environment</c> on the
/// server: <see cref="FlightEnvironments"/> at the centre of mass, then the server-only inputs carried by <see cref="Core.Protocol.Messages.ShipCorrection"/>
/// (gate, horizontal cap, a person in the hull), the terrain flags of the motion and the fuel reserve.
/// </summary>
public static class ClientFlightEnvironment
{
    /// <summary>Environment source in the frame of <paramref name="planet"/>.</summary>
    /// <param name="planet">Planet parameters (the same the server uses).</param>
    /// <param name="bodyIndex">Body index of the planet in the star system (0 without one).</param>
    /// <param name="rules">Flight rules.</param>
    /// <returns>The source.</returns>
    public static ShipEnvironmentSource InPlanet(PlanetParams planet, int bodyIndex, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(rules);
        return (in ShipMotion motion, ShipStats stats) => FlightEnvironments.InPlanet(planet, bodyIndex, CenterOfMass(motion, stats), rules);
    }

    /// <summary>Environment source in the system frame.</summary>
    /// <param name="system">The star system model of <c>ClientStarSystem</c>.</param>
    /// <param name="rules">Flight rules.</param>
    /// <returns>The source.</returns>
    public static ShipEnvironmentSource InSystem(StarSystemModel system, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(rules);
        return (in ShipMotion motion, ShipStats stats) => FlightEnvironments.InSystem(system, CenterOfMass(motion, stats), rules);
    }

    /// <summary>Environment source for the parent frame of the ship, with the same branches as the server.</summary>
    /// <param name="parent">Parent frame of the ship.</param>
    /// <param name="planet">Parameters of the planet when the parent is a planet; null when unknown.</param>
    /// <param name="system">Star system model; null before the system arrives.</param>
    /// <param name="rules">Flight rules.</param>
    /// <returns>The source.</returns>
    public static ShipEnvironmentSource For(FrameRef parent, PlanetParams? planet, StarSystemModel? system, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (parent.Kind == FrameKind.Planet && planet is not null)
        {
            var index = system is not null && system.TryGetBody(planet.Id, out var body) ? body.BodyIndex : 0;
            return InPlanet(planet, index, rules);
        }

        if (parent.Kind == FrameKind.System && system is not null)
        {
            return InSystem(system, rules);
        }

        return static (in ShipMotion _, ShipStats _) =>
            new FlightEnvironment { Up = new Vector3D(0, 1, 0), HorizontalSpeedCap = double.PositiveInfinity, Gate = ShipTerrainGate.Unavailable };
    }

    /// <summary>Centre of mass of the ship in the parent frame.</summary>
    /// <param name="motion">The motion.</param>
    /// <param name="stats">Ship stats.</param>
    /// <returns>The centre of mass.</returns>
    public static Vector3D CenterOfMass(in ShipMotion motion, ShipStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return motion.Pose.Position + motion.Pose.Rotation.Rotate(Vector3D.FromFloat(stats.CenterOfMass));
    }

    /// <summary>
    /// Completes <paramref name="environment"/> like the server step: gate and cap of the server, terrain flags on
    /// <paramref name="motion"/>, the fuel reserve, then a person in the hull for a launch attempt (parked, no reserve, gear up).
    /// </summary>
    /// <param name="environment">The environment from the source.</param>
    /// <param name="motion">The motion; its terrain flags are updated.</param>
    /// <param name="stats">Ship stats.</param>
    /// <param name="rules">Flight rules.</param>
    /// <param name="gate">Gate of the last correction.</param>
    /// <param name="horizontalCap">Horizontal cap of the last correction (+∞ = none).</param>
    /// <param name="personInHull">True when the last correction reported <see cref="ShipLaunchBlock.PersonInHull"/>.</param>
    /// <param name="control">Control of the step.</param>
    /// <returns>The environment of the step.</returns>
    public static FlightEnvironment Complete(
        in FlightEnvironment environment, ref ShipMotion motion, ShipStats stats, FlightRules rules, ShipTerrainGate gate, double horizontalCap,
        bool personInHull, in ShipControl control)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rules);
        var flags = motion.Flags & ~(ShipFlightFlags.TerrainHold | ShipFlightFlags.TerrainUnavailable);
        if (gate is ShipTerrainGate.NotLoaded or ShipTerrainGate.CollisionPending)
        {
            flags |= ShipFlightFlags.TerrainHold;
        }
        else if (gate == ShipTerrainGate.Unavailable)
        {
            flags |= ShipFlightFlags.TerrainUnavailable;
        }

        motion.Flags = flags;
        var parked = (flags & ShipFlightFlags.Parked) != 0;
        var block = parked && ShipFlightModel.IsReserve(motion.Fuel, stats, rules) ? ShipLaunchBlock.FuelReserve : ShipLaunchBlock.None;
        if (block == ShipLaunchBlock.None && personInHull && parked && (control.Buttons & ShipButtons.LandingGear) == 0)
        {
            block = ShipLaunchBlock.PersonInHull;
        }

        return environment with { Gate = gate, HorizontalSpeedCap = horizontalCap, LaunchBlock = block };
    }
}
