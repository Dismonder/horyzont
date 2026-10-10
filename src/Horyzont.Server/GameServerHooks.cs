using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
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

    /// <summary>Initializes a new instance of the <see cref="GameServerHooks"/> class.</summary>
    /// <param name="simulation">The simulation joins, intents and leaves are applied to.</param>
    /// <param name="acceptsChanges">False in safe mode and during the shutdown: joins throw and intents get <see cref="RejectReason.ServerReadOnly"/>.</param>
    /// <param name="logger">Logger.</param>
    public GameServerHooks(ServerSimulation simulation, Func<bool> acceptsChanges, ILogger<GameServerHooks> logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(acceptsChanges);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _acceptsChanges = acceptsChanges;
        _logger = logger;
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
            state.Id, state.CurrentTick, TickRate, player, joined.Entity, state.Planets.All, state.BlockTypes.Entries, state.Materials.Entries);
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player) => _logger.PlayerReady(player.Value);

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
        _logger.ClientMessage(player.Value, message.Type);
    }

    /// <inheritdoc />
    /// <remarks>Applied in safe mode too, so a restart does not find the player online; skipped only when the simulation is faulted.</remarks>
    public void OnPlayerLeft(PlayerId player, DisconnectCode reason)
    {
        _logger.PlayerLeft(player.Value, reason);
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
}
