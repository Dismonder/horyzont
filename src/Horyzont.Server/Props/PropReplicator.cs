using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Props;

/// <summary>
/// Replicates the harvested props by region (message 45, K6-31 D17). A player gets the full state of every region whose centre is within
/// <see cref="GameRules.PropSyncEnterMeters"/> of the body (empty regions too, which clears them at the client), and again the full state of a
/// region after every operation that changed it (<see cref="OperationProps"/>, so also a prop harvested by a dig or regrown inside a
/// compound). A region is dropped from the interest of a player beyond <see cref="GameRules.PropSyncLeaveMeters"/>, with no message: the client
/// forgets it by its own distance, which is larger, so coming back always brings a fresh state. The local player of a host shares the world of
/// the server and gets nothing. Created after <c>EntityReplication</c>, so the states follow its <see cref="OperationApplied"/>. Main thread only.
/// </summary>
public sealed class PropReplicator : IServerPlayerObserver, IServerTickParticipant
{
    /// <summary>Ticks between two scans of the regions around the players.</summary>
    public const int ScanEveryTicks = 30;

    /// <summary>Most regions one player gets in one scan; the rest follows with the next scans.</summary>
    public const int MaxRegionsPerPlayerPerScan = 256;

    private readonly Func<ServerSimulation> _simulation;
    private readonly Action<PlayerId, Message> _send;
    private readonly Func<PlayerId, bool> _isLocalPlayer;
    private readonly ILogger<PropReplicator> _logger;
    private readonly Dictionary<PlayerId, Interest> _players = [];
    private readonly PropInterest _interest = new();
    private readonly List<PropRegionKey> _candidates = [];
    private readonly List<PropRegionKey> _entering = [];
    private readonly List<ulong> _leaving = [];
    private readonly List<HarvestedPropRecord> _records = [];
    private readonly List<PropChange> _changes = [];
    private ServerSimulation? _attached;
    private ulong _lastScanTick = ulong.MaxValue;

    /// <summary>Initializes a new instance of the <see cref="PropReplicator"/> class.</summary>
    /// <param name="simulation">The simulation of the running world; asked when the first player is ready.</param>
    /// <param name="send">Sends a game message to a player of the session.</param>
    /// <param name="isLocalPlayer">True for the local player of a host.</param>
    /// <param name="logger">Logger.</param>
    public PropReplicator(Func<ServerSimulation> simulation, Action<PlayerId, Message> send, Func<PlayerId, bool> isLocalPlayer, ILogger<PropReplicator> logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(isLocalPlayer);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _send = send;
        _isLocalPlayer = isLocalPlayer;
        _logger = logger;
    }

    /// <summary>Creates the replicator for a server and registers it as player observer and tick participant; call before the world starts.</summary>
    /// <param name="runtime">The server.</param>
    /// <param name="loggers">Loggers.</param>
    /// <returns>The replicator.</returns>
    public static PropReplicator Register(ServerRuntime runtime, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(loggers);
        var replicator = new PropReplicator(
            () => runtime.Simulation, (player, message) => runtime.Session.Send(player, message), runtime.IsLocalPlayer, loggers.CreateLogger<PropReplicator>());
        runtime.AddPlayerObserver(replicator);
        runtime.AddTickParticipant(replicator);
        return replicator;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        Attach(_simulation());
        if (_isLocalPlayer(player))
        {
            _players.Remove(player);
            return;
        }

        // A new session knows nothing: its interest starts empty and is scanned at the next tick, whatever the phase of the scan period.
        _players[player] = new Interest { NeedsScan = true };
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => _players.Remove(player);

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Tick();
    }

    /// <summary>One tick of the replicator: scans the players that are new and, every <see cref="ScanEveryTicks"/>th tick, all of them.</summary>
    public void Tick()
    {
        if (_players.Count == 0)
        {
            return;
        }

        var sim = _simulation();
        Attach(sim);
        if (sim.Survival is not { Props.Count: > 0 } || sim.IsFaulted)
        {
            return;
        }

        var tick = sim.State.CurrentTick.Value;
        var periodic = tick % ScanEveryTicks == 0 && tick != _lastScanTick;
        _lastScanTick = periodic ? tick : _lastScanTick;
        foreach (var pair in _players)
        {
            if (periodic || pair.Value.NeedsScan)
            {
                Scan(sim, pair.Key, pair.Value);
            }
        }
    }

    private static int CountEntries(List<PropRegionState> regions)
    {
        var total = 0;
        foreach (var region in regions)
        {
            total += region.Props.Count;
        }

        return total;
    }

    private void Attach(ServerSimulation sim)
    {
        if (_attached == sim)
        {
            return;
        }

        if (_attached is not null)
        {
            _attached.Applied -= OnApplied;
        }

        _attached = sim;
        sim.Applied += OnApplied;
    }

    private void Scan(ServerSimulation sim, PlayerId player, Interest interest)
    {
        interest.NeedsScan = false;
        var state = sim.State;
        var rules = sim.Rules;
        if (!state.Players.TryGet(player, out var body) || !body.IsOnline
            || !state.TryToPlanetFrame(body.Frame, body.Position, out var planetId, out var feet)
            || !state.Planets.TryGet(planetId, out var planet) || !PropLayouts.TryGet(planet.GeneratorProfile, out _))
        {
            // Not on a planet with props (in space, in a ship far away): nothing is in range, and the client forgets by its own distance.
            interest.Regions.Clear();
            return;
        }

        if (interest.Planet != planetId)
        {
            interest.Regions.Clear();
            interest.Planet = planetId;
        }

        // Out of range first, with the larger distance of leaving; no message.
        _leaving.Clear();
        foreach (var value in interest.Regions)
        {
            if (PropInterest.DistanceToRegion(planet, feet, new PropRegionKey(value)) > rules.PropSyncLeaveMeters)
            {
                _leaving.Add(value);
            }
        }

        foreach (var value in _leaving)
        {
            interest.Regions.Remove(value);
        }

        // Then the regions that came in range, the lowest identifiers first, at most MaxRegionsPerPlayerPerScan; the rest waits for the next scan.
        _interest.CollectRegions(planet, feet, rules.PropSyncEnterMeters, _candidates);
        _entering.Clear();
        foreach (var region in _candidates)
        {
            if (_entering.Count >= MaxRegionsPerPlayerPerScan)
            {
                break;
            }

            if (interest.Regions.Add(region.Value))
            {
                _entering.Add(region);
            }
        }

        if (_entering.Count > 0)
        {
            SendRegions(state, player, planetId, _entering);
            _logger.RegionsSent(_entering.Count, planetId.Value, player.Value);
        }
    }

    private void OnApplied(AppliedOperation applied)
    {
        if (_players.Count == 0)
        {
            return;
        }

        _changes.Clear();
        OperationProps.CollectChanges(applied.Operation, _changes);
        if (_changes.Count == 0)
        {
            return;
        }

        var state = _attached!.State;
        var changed = new List<(PlanetId Planet, PropRegionKey Region)>();
        foreach (var change in _changes)
        {
            var key = (change.Planet, change.Prop.Region);
            if (!changed.Contains(key))
            {
                changed.Add(key);
            }
        }

        changed.Sort(static (a, b) =>
        {
            var planet = a.Planet.Value.CompareTo(b.Planet.Value);
            return planet != 0 ? planet : a.Region.Value.CompareTo(b.Region.Value);
        });

        var recipients = 0;
        foreach (var pair in _players)
        {
            var regions = new List<PropRegionKey>();
            PlanetId planet = default;
            foreach (var (changedPlanet, region) in changed)
            {
                if (changedPlanet == pair.Value.Planet && pair.Value.Regions.Contains(region.Value))
                {
                    planet = changedPlanet;
                    regions.Add(region);
                }
            }

            if (regions.Count > 0)
            {
                SendRegions(state, pair.Key, planet, regions);
                recipients++;
            }
        }

        _logger.RegionsChanged(changed.Count, applied.Id.Value, recipients);
    }

    /// <summary>Sends the full state of regions in messages of at most 64 regions and 4096 entries.</summary>
    private void SendRegions(WorldState state, PlayerId player, PlanetId planet, List<PropRegionKey> regions)
    {
        var batch = new List<PropRegionState>(Math.Min(regions.Count, ProtocolConstants.MaxPropRegionsPerMessage));
        foreach (var region in regions)
        {
            _records.Clear();
            state.HarvestedProps.CollectRegion(planet, region, _records);
            var entries = new HarvestedPropEntry[_records.Count];
            for (var i = 0; i < entries.Length; i++)
            {
                entries[i] = new HarvestedPropEntry(_records[i].Prop, _records[i].Tick);
            }

            if (batch.Count > 0
                && (batch.Count >= ProtocolConstants.MaxPropRegionsPerMessage || CountEntries(batch) + entries.Length > ProtocolConstants.MaxPropEntriesPerMessage))
            {
                _send(player, new PropStates(planet, batch));
                batch = new List<PropRegionState>(ProtocolConstants.MaxPropRegionsPerMessage);
            }

            batch.Add(new PropRegionState(region, entries));
        }

        if (batch.Count > 0)
        {
            _send(player, new PropStates(planet, batch));
        }
    }

    private sealed class Interest
    {
        public PlanetId Planet { get; set; }

        public bool NeedsScan { get; set; }

        public HashSet<ulong> Regions { get; } = [];
    }
}
