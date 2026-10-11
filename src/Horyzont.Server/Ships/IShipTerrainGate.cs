using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Server.Ships;

/// <summary>
/// What the terrain under a ship in a planet frame allows (K5-24 decision 17, README Y11), asked once per tick per ship by the
/// flight. Stateful per ship (readiness timeout, retries, logs). Main thread.
/// </summary>
public interface IShipTerrainGate
{
    /// <summary>Gets the gate of a ship for this tick.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="planet">Its parent planet.</param>
    /// <param name="motion">Its motion in the planet frame.</param>
    /// <param name="stats">Its stats.</param>
    /// <param name="horizontalSpeedCap">Receives the horizontal speed cap near the terrain (README Y11; infinite when none).</param>
    /// <returns>The gate.</returns>
    ShipTerrainGate Query(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, out double horizontalSpeedCap);

    /// <summary>
    /// Gets the readiness of the ground under a ship down to a depth (the load settler sweeps its whole drop through it, decision
    /// 18): <see cref="ShipTerrainGate.Ready"/>; <see cref="ShipTerrainGate.CollisionPending"/> while it streams;
    /// <see cref="ShipTerrainGate.NotLoaded"/> without terrain or after <see cref="FlightRules.ReadinessTimeoutSeconds"/> without
    /// Ready (nothing loads the ground that far under a held ship: the flight takes over); <see cref="ShipTerrainGate.Unavailable"/>
    /// for a faulted terrain.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="planet">Its parent planet.</param>
    /// <param name="motion">Its motion in the planet frame.</param>
    /// <param name="stats">Its stats.</param>
    /// <param name="depthMeters">Depth of the column under the centre of the ship.</param>
    /// <returns>The gate of the column.</returns>
    ShipTerrainGate QueryGround(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, double depthMeters);

    /// <summary>Drops the state kept for a ship.</summary>
    /// <param name="ship">The ship.</param>
    void Forget(EntityId ship);
}

/// <summary>
/// The gate without terrain readiness (decision 17, P6): the home planet is ready, other bodies have no terrain, so a ship stays
/// above 2 R there.
/// </summary>
/// <param name="home">The home planet.</param>
public sealed class HomeOnlyGate(PlanetId home) : IShipTerrainGate
{
    /// <inheritdoc />
    public ShipTerrainGate Query(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, out double horizontalSpeedCap)
    {
        horizontalSpeedCap = double.PositiveInfinity;
        return planet == home ? ShipTerrainGate.Ready : ShipTerrainGate.NotLoaded;
    }

    /// <inheritdoc />
    public ShipTerrainGate QueryGround(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, double depthMeters)
        => planet == home ? ShipTerrainGate.Ready : ShipTerrainGate.NotLoaded;

    /// <inheritdoc />
    public void Forget(EntityId ship)
    {
    }
}
