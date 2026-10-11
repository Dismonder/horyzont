using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Frames;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Space;

/// <summary>
/// Moves ships between the system frame and the planet frames at the borders of the zones of influence (K5-21 decision 16, spec
/// 02:158): a ship in the system frame enters the frame of the body whose 3.0 R zone it reaches; a ship in a planet frame leaves for
/// the system frame beyond 3.2 R. Only <see cref="ConstructMode.Ship"/> constructs and only through <see cref="FrameTransitions"/>;
/// passengers keep their ship frame. Phase <see cref="ServerTickPhase.Frames"/>, after the flight of ships (K5-24 registers it first).
/// </summary>
public sealed class ServerZoneTransitions : IServerTickParticipant
{
    /// <summary>Seconds between two failure logs of the same ship.</summary>
    public const int FailureLogIntervalSeconds = 5;

    private readonly Func<FrameTransitions?> _transitions;
    private readonly ulong _logIntervalTicks;
    private readonly ILogger<ServerZoneTransitions> _logger;
    private readonly List<EntityId> _ships = [];
    private readonly Dictionary<EntityId, ulong> _lastFailureLog = [];
    private StarSystemModel? _model;

    /// <summary>Initializes a new instance of the <see cref="ServerZoneTransitions"/> class.</summary>
    /// <param name="transitions">The frame transitions of the running world; null before the world starts.</param>
    /// <param name="tickRate">Server ticks per second (failure log interval).</param>
    /// <param name="logger">Logger.</param>
    public ServerZoneTransitions(Func<FrameTransitions?> transitions, int tickRate, ILogger<ServerZoneTransitions> logger)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickRate, 0);
        _transitions = transitions;
        _logIntervalTicks = (ulong)(tickRate * FailureLogIntervalSeconds);
        _logger = logger;
    }

    /// <inheritdoc />
    public ServerTickPhase Phase => ServerTickPhase.Frames;

    /// <summary>Gets the number of reparents done.</summary>
    public int Reparented { get; private set; }

    /// <summary>Gets the number of refused reparents (retried next tick).</summary>
    public int Refused { get; private set; }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Step(runtime.State);
    }

    /// <summary>One tick of zone checks; a world without a star system does nothing.</summary>
    /// <param name="state">The running world.</param>
    public void Step(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.StarSystem is not { } system || _transitions() is not { } transitions)
        {
            return;
        }

        if (_model is null || !ReferenceEquals(_model.System, system))
        {
            _model = new StarSystemModel(system, state.Planets.All);
        }

        // Ids first: a reparent changes the construct while the store is read (constructs are sorted by id).
        _ships.Clear();
        var all = state.Constructs.All;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Mode == ConstructMode.Ship)
            {
                _ships.Add(all[i].Id);
            }
        }

        var systemFrame = FrameRef.Of(system.System);
        for (var i = 0; i < _ships.Count; i++)
        {
            if (!state.Constructs.TryGet(_ships[i], out var ship))
            {
                continue;
            }

            if (TryTarget(ship, systemFrame, transitions.Graph, _model, out var target))
            {
                Reparent(transitions, ship.Id, target, state.CurrentTick.Value);
            }
        }
    }

    private static bool TryTarget(Construct ship, FrameRef systemFrame, FrameGraph graph, StarSystemModel model, out FrameRef target)
    {
        target = default;
        switch (ship.Parent.Kind)
        {
            case FrameKind.System:
                if (graph.TryGetWorldPose(FrameRef.Of(ship.Id), out var pose) && model.TryFindZone(pose.Position, out var index, out _))
                {
                    target = FrameRef.Of(model.GetBody(index).Planet);
                    return true;
                }

                return false;

            case FrameKind.Planet:
                // The origin of the ship frame is the reference point (P6); ships are tiny against the 0.2 R band.
                if (model.TryGetBody(new PlanetId(ship.Parent.Id), out var body)
                    && Vector3D.FromFloat(ship.Origin).Length() > StarSystemRules.ZoneExitFactor * body.RadiusMeters)
                {
                    target = systemFrame;
                    return true;
                }

                return false;

            default:
                // A ship docked in another construct follows its parent.
                return false;
        }
    }

    private void Reparent(FrameTransitions transitions, EntityId ship, FrameRef target, ulong tick)
    {
        if (transitions.TryReparent(ship, target, null, out _, out var failure))
        {
            Reparented++;
            _lastFailureLog.Remove(ship);
            SpaceLog.ShipReparented(_logger, ship.Value, target.Kind, target.Id);
            return;
        }

        Refused++;
        if (!_lastFailureLog.TryGetValue(ship, out var last) || tick - last >= _logIntervalTicks)
        {
            _lastFailureLog[ship] = tick;
            SpaceLog.ZoneTransitionRefused(_logger, ship.Value, target.Kind, target.Id, failure);
        }
    }
}
