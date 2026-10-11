using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Net.Session;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server;

/// <summary>Production session hooks over a <see cref="ServerSimulation"/>; main thread only.</summary>
public sealed class GameServerHooks : IServerGameHooks
{
    /// <summary>Ticks per second announced to clients (physics runs at 60 Hz).</summary>
    public const byte TickRate = 60;

    private readonly ServerSimulation _simulation;
    private readonly Func<bool> _acceptsChanges;
    private readonly ILogger<GameServerHooks> _logger;
    private readonly IReadOnlyList<IServerPlayerObserver> _observers;
    private readonly NewPlayerSpawn? _spawn;
    private readonly IReadOnlyList<IServerClientMessageHandler> _handlers;

    /// <summary>Initializes a new instance of the <see cref="GameServerHooks"/> class.</summary>
    /// <param name="simulation">The simulation joins, intents and leaves are applied to.</param>
    /// <param name="acceptsChanges">False in safe mode and during the shutdown: joins throw and intents get <see cref="RejectReason.ServerReadOnly"/>.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="observers">Systems told when a player is ready and before a player leaves; none when null.</param>
    /// <param name="spawn">Pose of a player who joins for the first time; the simulation's own spawn point when null.</param>
    /// <param name="handlers">Client message handlers in registration order; none when null.</param>
    public GameServerHooks(
        ServerSimulation simulation, Func<bool> acceptsChanges, ILogger<GameServerHooks> logger,
        IReadOnlyList<IServerPlayerObserver>? observers = null, NewPlayerSpawn? spawn = null, IReadOnlyList<IServerClientMessageHandler>? handlers = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(acceptsChanges);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _acceptsChanges = acceptsChanges;
        _logger = logger;
        _observers = observers ?? [];
        _spawn = spawn;
        _handlers = handlers ?? [];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Prepare and apply happen in this one call, so two joins in one poll never get the same prepared player id (E5). Nothing is
    /// applied when this throws (E4).
    /// </remarks>
    /// <exception cref="InvalidOperationException">The server is read-only, or the player is already online.</exception>
    public PlayerId OnPlayerJoining(PlatformIdentity identity, string displayName)
    {
        if (!_acceptsChanges())
        {
            throw new InvalidOperationException("server is read-only");
        }

        var operation = _simulation.PreparePlayerJoin(identity, displayName);
        if (_spawn is not null && !_simulation.State.Players.TryGetByIdentity(identity, out _)
            && _simulation.State.Planets.TryGet(new PlanetId(operation.Frame.Id), out var planet))
        {
            // A new identity starts where the game wants it (K2-11: the equator); a known one returns to its saved pose.
            var (feet, rotation) = _spawn(planet);
            operation = operation with { Position = feet, BodyRotation = rotation };
        }

        if (_simulation.State.Players.TryGet(operation.Player, out var known) && known.IsOnline)
        {
            throw new InvalidOperationException($"player {operation.Player.Value} is already online");
        }

        _simulation.ApplyServerOperation(operation);
        _logger.PlayerJoining(operation.Player.Value, displayName);
        return operation.Player;
    }

    /// <inheritdoc />
    public ServerHello CreateHello(PlayerId player)
    {
        var state = _simulation.State;
        if (!state.Players.TryGet(player, out var joined))
        {
            throw new InvalidOperationException($"player {player.Value} is unknown");
        }

        return new ServerHello(
            state.Id, state.CurrentTick, TickRate, player, joined.Entity, HomeFirst(state), state.BlockTypes.Entries, state.Materials.Entries);
    }

    // K5-21 decision 10: element 0 of ServerHello.Planets is the home planet, the rest keeps the registry (id) order.
    private static List<PlanetParams> HomeFirst(WorldState state)
    {
        var all = state.Planets.All;
        var planets = new List<PlanetParams>(all.Count);
        if (state.Planets.TryGet(state.HomePlanet, out var home))
        {
            planets.Add(home);
        }

        foreach (var planet in all)
        {
            if (planet.Id != state.HomePlanet)
            {
                planets.Add(planet);
            }
        }

        return planets;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        _logger.PlayerReady(player.Value);
        foreach (var observer in _observers)
        {
            observer.OnPlayerReady(player);
        }
    }

    /// <inheritdoc />
    public IntentReply OnIntent(PlayerId player, uint clientSeq, Intent intent)
    {
        if (!_acceptsChanges())
        {
            _logger.IntentRefusedReadOnly(player.Value, clientSeq);
            return new IntentReply(clientSeq, RejectReason.ServerReadOnly, OpId.None);
        }

        var result = _simulation.Submit(player, intent);
        return new IntentReply(clientSeq, result.Reason, result.Applied?.Id ?? OpId.None);
    }

    /// <inheritdoc />
    public void OnClientMessage(PlayerId player, Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var handled = false;
        for (var i = 0; i < _handlers.Count; i++)
        {
            handled |= _handlers[i].Handle(player, message);
        }

        if (!handled)
        {
            _logger.ClientMessage(player.Value, message.Type);
        }
    }

    /// <inheritdoc />
    /// <remarks>Applied in safe mode too, so a restart does not find the player online; skipped only when the simulation is faulted.</remarks>
    public void OnPlayerLeft(PlayerId player, DisconnectCode reason)
    {
        _logger.PlayerLeft(player.Value, reason);
        foreach (var observer in _observers)
        {
            NotifyLeaving(observer, player, reason);
        }

        if (_simulation.IsFaulted)
        {
            _logger.LeaveSkipped(player.Value);
            return;
        }

        if (_simulation.State.Players.TryGet(player, out var state) && state.IsOnline)
        {
            // The leave op is serialized with a strict length check; normalize what physics left slightly off (lesson 3c).
            var rotation = Quaternion.Normalize(state.BodyRotation);
            _simulation.ApplyServerOperation(new PlayerLeaveOp(player, state.Frame, state.ParentSeq, state.Position, rotation, state.Pitch));
        }
    }

    private void NotifyLeaving(IServerPlayerObserver observer, PlayerId player, DisconnectCode reason)
    {
#pragma warning disable CA1031 // The leave must reach the journal even when an observer fails; the failure is logged and the observer cleans up on its own shutdown.
        try
        {
            observer.OnPlayerLeaving(player, reason);
        }
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.ObserverFailed(player.Value, ex);
        }
    }
}
