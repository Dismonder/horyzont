using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Ships;

/// <summary>Result of one settling step.</summary>
public enum ShipSettleResult
{
    /// <summary>The ship is held in place (v = 0) waiting for the terrain.</summary>
    Holding,

    /// <summary>The ship is settled: parked on the ground, stopped in space, or left to the flight when no ground is under it.</summary>
    Settled,
}

/// <summary>
/// Settling of ships loaded unparked (K5-24 decision 18), before their first flight step: in a planet frame the ship is held with
/// v = 0 (as an unparked ship it demands the terrain itself) until the gate is <see cref="ShipTerrainGate.Ready"/>, then swept
/// down by its height over the lowest ground plus <see cref="ExtraDropMeters"/> and parked where it hits (no hit: the flight takes
/// over and autolands); an unavailable terrain keeps it held (the gate retries and logs). In the system frame it stops in place.
/// Changes leave the journal out (the flight stores them). Main thread.
/// </summary>
/// <param name="collider">Hull collision.</param>
/// <param name="rules">Flight rules.</param>
/// <param name="logger">Logger.</param>
public sealed class ShipLoadSettler(IShipCollider collider, FlightRules rules, ILogger logger)
{
    /// <summary>Extra drop under the lowest possible ground (metres).</summary>
    public const double ExtraDropMeters = 10;

    private readonly IShipCollider _collider = collider ?? throw new ArgumentNullException(nameof(collider));
    private readonly FlightRules _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>One settling step.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="motion">Its motion (pose, velocities and flags are changed).</param>
    /// <param name="stats">Its stats.</param>
    /// <param name="environment">Its environment with the gate.</param>
    /// <param name="inPlanet">True in a planet frame.</param>
    /// <returns>Whether the ship is settled.</returns>
    public ShipSettleResult Settle(EntityId ship, ref ShipMotion motion, ShipStats stats, in FlightEnvironment environment, bool inPlanet)
    {
        ArgumentNullException.ThrowIfNull(stats);
        motion.LinearVelocity = default;
        motion.AngularVelocity = default;
        if (!inPlanet)
        {
            return ShipSettleResult.Settled;
        }

        if (environment.Gate != ShipTerrainGate.Ready)
        {
            return ShipSettleResult.Holding;
        }

        var drop = Math.Max(0, environment.DistanceFromBody - environment.RadiusOfBody) + _rules.SurfaceClearanceMeters + ExtraDropMeters;
        var from = motion.Pose;
        var to = new PoseD(from.Position - (environment.Up * drop), from.Rotation);
        var hit = _collider.Sweep(ship, from, to);
        if (!hit.Hit)
        {
            return ShipSettleResult.Settled;
        }

        motion.Pose = new PoseD(from.Position + ((to.Position - from.Position) * hit.Fraction), from.Rotation);
        motion.Flags |= ShipFlightFlags.Parked | ShipFlightFlags.GearDown;
        _logger.ShipSettled(ship.Value, drop * hit.Fraction);
        return ShipSettleResult.Settled;
    }
}
