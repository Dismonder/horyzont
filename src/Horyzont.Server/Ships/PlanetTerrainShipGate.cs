using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World;
using Horyzont.Server.Space;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Ships;

/// <summary>
/// The terrain gate over <see cref="IPlanetTerrainReadiness"/> (K5-24 decision 17, K5-22 decision 6, README Y11): the readiness of
/// the ship query (<see cref="ShipTerrainQuery"/>: the column under the ship and, near the ground, the sweep box) mapped to the gate:
/// Ready → <see cref="ShipTerrainGate.Ready"/>; Inactive, or Streaming high above the ground
/// (<c>Altitude ≥ 2 s · v_lim + CollisionPendingAltitude</c>) → <see cref="ShipTerrainGate.NotLoaded"/>; Streaming lower →
/// <see cref="ShipTerrainGate.CollisionPending"/>; Faulted, or no Ready for <see cref="FlightRules.ReadinessTimeoutSeconds"/>
/// (counted in queries per ship, reset by Ready, a planet change and parking) → <see cref="ShipTerrainGate.Unavailable"/>, which
/// asks again every <see cref="RetrySeconds"/> and logs a warning at most every <see cref="LogIntervalSeconds"/> per ship. In the band
/// near the terrain the horizontal speed is capped by <see cref="TerrainSweep.MaxHorizontalSpeed"/>. Main thread.
/// </summary>
public sealed class PlanetTerrainShipGate : IShipTerrainGate
{
    /// <summary>Seconds between two queries of an unavailable terrain.</summary>
    public const int RetrySeconds = 5;

    /// <summary>Seconds between two warnings for the same ship.</summary>
    public const int LogIntervalSeconds = 10;

    private readonly IPlanetTerrainReadiness _readiness;
    private readonly WorldState _state;
    private readonly FlightRules _rules;
    private readonly float _maxRelief;
    private readonly int _tickRate;
    private readonly ILogger _logger;
    private readonly Dictionary<EntityId, Entry> _entries = [];

    /// <summary>Initializes a new instance of the <see cref="PlanetTerrainShipGate"/> class.</summary>
    /// <param name="readiness">Terrain readiness (K5-22 <c>ServerPlanetTerrains</c>).</param>
    /// <param name="state">The world (planet radius).</param>
    /// <param name="rules">Flight rules.</param>
    /// <param name="maxReliefMeters">Highest relief of the generator over R.</param>
    /// <param name="tickRate">Server ticks per second (one query per ship and tick).</param>
    /// <param name="logger">Logger.</param>
    public PlanetTerrainShipGate(
        IPlanetTerrainReadiness readiness, WorldState state, FlightRules rules, float maxReliefMeters, int tickRate, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickRate, 0);
        _readiness = readiness;
        _state = state;
        _rules = rules;
        _maxRelief = maxReliefMeters;
        _tickRate = tickRate;
        _logger = logger;
    }

    /// <summary>Gets the number of readiness queries made (the retry interval of an unavailable terrain).</summary>
    public int Queries { get; private set; }

    /// <summary>Gets the number of warnings logged.</summary>
    public int Warnings { get; private set; }

    /// <summary>Gets the last query (tests: the column geometry).</summary>
    public ShipTerrainQuery LastQuery { get; private set; }

    /// <inheritdoc />
    public ShipTerrainGate Query(EntityId ship, PlanetId planet, in ShipMotion motion, ShipStats stats, out double horizontalSpeedCap)
    {
        ArgumentNullException.ThrowIfNull(stats);
        horizontalSpeedCap = double.PositiveInfinity;
        if (!_state.Planets.TryGet(planet, out var body))
        {
            return ShipTerrainGate.NotLoaded;
        }

        if (!_entries.TryGetValue(ship, out var entry))
        {
            entry = new Entry();
            _entries.Add(ship, entry);
        }

        entry.Ticks++;
        if (entry.Planet != planet || (motion.Flags & ShipFlightFlags.Parked) != 0)
        {
            entry.Planet = planet;
            entry.Waited = 0;
            entry.Unavailable = false;
        }

        var com = motion.Pose.Position + motion.Pose.Rotation.Rotate(Vector3D.FromFloat(stats.CenterOfMass));
        var center = new Vector3((float)com.X, (float)com.Y, (float)com.Z);
        var radius = stats.BoundingRadius;
        var altitude = MathF.Max(center.Length() - (body.RadiusMeters - _maxRelief), 0f);
        var query = ShipTerrainQuery.Of(center, radius, motion.LinearVelocity, altitude, body.RadiusMeters, _maxRelief);
        LastQuery = query;
        if (query.HasSweep)
        {
            horizontalSpeedCap = TerrainSweep.MaxHorizontalSpeed(radius);
        }

        entry.Waited++;
        if (entry.Unavailable && entry.Waited % (RetrySeconds * _tickRate) != 0)
        {
            Warn(ship, planet, entry, entry.Last);
            return ShipTerrainGate.Unavailable;
        }

        Queries++;
        var readiness = _readiness.GetShipReadiness(planet, query);
        entry.Last = readiness;
        if (readiness == PlanetTerrainReadiness.Ready)
        {
            entry.Waited = 0;
            entry.Unavailable = false;
            return ShipTerrainGate.Ready;
        }

        if (readiness == PlanetTerrainReadiness.Faulted || entry.Waited >= _rules.ReadinessTimeoutSeconds * _tickRate)
        {
            entry.Unavailable = true;
            Warn(ship, planet, entry, readiness);
            return ShipTerrainGate.Unavailable;
        }

        var surfaceAltitude = Math.Max(0, com.Length() - body.RadiusMeters - _rules.SurfaceClearanceMeters);
        var limit = _rules.SpeedLimit(surfaceAltitude, (motion.Flags & ShipFlightFlags.PulseEngaged) != 0);
        return readiness == PlanetTerrainReadiness.Inactive || surfaceAltitude >= (2 * limit) + _rules.CollisionPendingAltitude
            ? ShipTerrainGate.NotLoaded
            : ShipTerrainGate.CollisionPending;
    }

    /// <inheritdoc />
    public void Forget(EntityId ship) => _entries.Remove(ship);

    private void Warn(EntityId ship, PlanetId planet, Entry entry, PlanetTerrainReadiness readiness)
    {
        if (entry.LastLog is { } last && entry.Ticks - last < (long)LogIntervalSeconds * _tickRate)
        {
            return;
        }

        entry.LastLog = entry.Ticks;
        Warnings++;
        _logger.TerrainUnavailable(ship.Value, planet.Value, readiness, entry.Waited / (double)_tickRate);
    }

    private sealed class Entry
    {
        public PlanetId Planet { get; set; }

        public long Ticks { get; set; }

        public long Waited { get; set; }

        public bool Unavailable { get; set; }

        public PlanetTerrainReadiness Last { get; set; }

        public long? LastLog { get; set; }
    }
}
