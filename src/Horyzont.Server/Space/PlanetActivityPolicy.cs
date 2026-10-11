using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Space;

/// <summary>
/// Decides which planets have terrain on the server (K5-22 decision 2). Every <see cref="PlanetActivityOptions.EvaluationIntervalTicks"/>
/// it gathers the demand: (a) the region of every online player (a passenger inherits the region of its ship), (b) ship-mode
/// constructs in a planet frame that <see cref="IShipDemandSource"/> says demand terrain, (c) leases, (d) the home planet, always.
/// A planet without demand is deactivated after <see cref="PlanetActivityOptions.LingerTicks"/>; demand within that time cancels the
/// countdown. Results are sorted by planet id. Time is server ticks. Pure; allocation-free after the first evaluation.
/// </summary>
public sealed class PlanetActivityPolicy
{
    private readonly List<PlanetId> _activate = [];
    private readonly List<PlanetId> _deactivate = [];
    private PlanetId[] _planets = [];
    private bool[] _active = [];
    private bool[] _demanded = [];
    private ulong[] _lastDemand = [];
    private int[] _leases = [];
    private bool _evaluated;
    private ulong _lastEvaluation;

    /// <summary>Initializes a new instance of the <see cref="PlanetActivityPolicy"/> class.</summary>
    /// <param name="options">Settings; defaults when null.</param>
    public PlanetActivityPolicy(PlanetActivityOptions? options = null)
    {
        Options = options ?? new PlanetActivityOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Options.EvaluationIntervalTicks, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(Options.LingerTicks);
    }

    /// <summary>Gets the settings.</summary>
    public PlanetActivityOptions Options { get; }

    /// <summary>Gets the planets to activate found by the last call of <see cref="Update"/>, sorted by id.</summary>
    public IReadOnlyList<PlanetId> Activate => _activate;

    /// <summary>Gets the planets to deactivate found by the last call of <see cref="Update"/>, sorted by id.</summary>
    public IReadOnlyList<PlanetId> Deactivate => _deactivate;

    /// <summary>Gets a value indicating whether a planet is active by the policy.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>True when active.</returns>
    public bool IsActive(PlanetId planet)
    {
        var i = IndexOf(planet);
        return i >= 0 && _active[i];
    }

    /// <summary>Gets the number of live leases of a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The count.</returns>
    public int LeaseCount(PlanetId planet)
    {
        var i = IndexOf(planet);
        return i >= 0 ? _leases[i] : 0;
    }

    /// <summary>Adds a lease: the planet is demanded from now on and becomes active at once (the caller creates its terrain).</summary>
    /// <param name="state">The world (its planet registry).</param>
    /// <param name="planet">The planet.</param>
    /// <returns>True when the planet was inactive and became active.</returns>
    /// <exception cref="ArgumentException">The planet is unknown.</exception>
    public bool AddLease(WorldState state, PlanetId planet)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsurePlanets(state);
        var i = IndexOf(planet);
        if (i < 0)
        {
            throw new ArgumentException($"Unknown planet {planet}.", nameof(planet));
        }

        _leases[i]++;
        if (_active[i])
        {
            return false;
        }

        _active[i] = true;
        return true;
    }

    /// <summary>Releases a lease; the usual linger countdown starts at <paramref name="tick"/> when nothing else demands the planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="tick">The current tick.</param>
    /// <exception cref="InvalidOperationException">The planet has no lease.</exception>
    public void ReleaseLease(PlanetId planet, Tick tick)
    {
        var i = IndexOf(planet);
        if (i < 0 || _leases[i] == 0)
        {
            throw new InvalidOperationException($"Planet {planet} has no lease to release.");
        }

        _leases[i]--;
        if (_leases[i] == 0)
        {
            _lastDemand[i] = Math.Max(_lastDemand[i], tick.Value);
        }
    }

    /// <summary>
    /// Evaluates the demand when the interval elapsed (the first call always evaluates). <see cref="Activate"/> and
    /// <see cref="Deactivate"/> are cleared on every call and filled only by an evaluation.
    /// </summary>
    /// <param name="tick">The current tick.</param>
    /// <param name="state">The world.</param>
    /// <param name="graph">The frame graph of the world.</param>
    /// <param name="ships">Demand of ship-mode constructs.</param>
    /// <returns>True when this call evaluated.</returns>
    public bool Update(Tick tick, WorldState state, FrameGraph graph, IShipDemandSource ships)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(ships);
        _activate.Clear();
        _deactivate.Clear();
        if (_evaluated && tick.Value - _lastEvaluation < (ulong)Options.EvaluationIntervalTicks)
        {
            return false;
        }

        _evaluated = true;
        _lastEvaluation = tick.Value;
        EnsurePlanets(state);
        Array.Clear(_demanded);
        Demand(state.HomePlanet);

        var players = state.Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.IsOnline && graph.TryGetRegion(player.Frame, out var region) && region.Kind == FrameKind.Planet)
            {
                Demand(new PlanetId(region.Id));
            }
        }

        var constructs = state.Constructs.All;
        for (var i = 0; i < constructs.Count; i++)
        {
            var construct = constructs[i];
            if (construct.Mode == ConstructMode.Ship && construct.Parent.Kind == FrameKind.Planet && ships.Demands(construct))
            {
                Demand(new PlanetId(construct.Parent.Id));
            }
        }

        for (var i = 0; i < _planets.Length; i++)
        {
            if (_demanded[i] || _leases[i] > 0)
            {
                _lastDemand[i] = tick.Value;
                if (!_active[i])
                {
                    _active[i] = true;
                    _activate.Add(_planets[i]);
                }
            }
            else if (_active[i] && tick.Value - _lastDemand[i] >= (ulong)Options.LingerTicks)
            {
                _active[i] = false;
                _deactivate.Add(_planets[i]);
            }
        }

        return true;
    }

    private void Demand(PlanetId planet)
    {
        var i = IndexOf(planet);
        if (i >= 0)
        {
            _demanded[i] = true;
        }
    }

    private int IndexOf(PlanetId planet)
    {
        // Planets are few (four in EA) and sorted by id.
        for (var i = 0; i < _planets.Length; i++)
        {
            if (_planets[i] == planet)
            {
                return i;
            }
        }

        return -1;
    }

    private void EnsurePlanets(WorldState state)
    {
        var all = state.Planets.All;
        if (all.Count == _planets.Length)
        {
            return;
        }

        // The planet registry of a running world does not change; this runs once.
        var planets = new PlanetId[all.Count];
        for (var i = 0; i < all.Count; i++)
        {
            planets[i] = all[i].Id;
        }

        _planets = planets;
        _active = new bool[planets.Length];
        _demanded = new bool[planets.Length];
        _lastDemand = new ulong[planets.Length];
        _leases = new int[planets.Length];
    }
}
