using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Crafting;

/// <summary>
/// Tells a player what the player discovered and which progress keys the world has (K6-32 D11): <see cref="PlayerProgressState"/> when the player
/// is ready (after the container states; items held from before the discoveries existed are discovered first) and after every operation that
/// discovers an item for the player (to the owner only) or grants a progress key (to every player online). Recognizes the operations by
/// <see cref="OperationProgress"/>, because both are components of compound operations. Never changes the world from the handler of
/// <see cref="ServerSimulation.Applied"/>. Main thread only.
/// </summary>
public sealed class ProgressReplication : IServerPlayerObserver
{
    private readonly Func<ServerSimulation?> _simulation;
    private readonly Action<PlayerId, Message> _send;
    private readonly ILogger _logger;
    private readonly Dictionary<PlayerId, bool> _players = [];
    private readonly List<PlayerId> _discoveredBy = [];
    private readonly List<PlayerId> _targets = [];
    private ServerSimulation? _attached;

    /// <summary>Initializes a new instance of the <see cref="ProgressReplication"/> class.</summary>
    /// <param name="simulation">The simulation of the running world (null before the world has started).</param>
    /// <param name="send">Sends a message to one player.</param>
    /// <param name="logger">Logger, null for none.</param>
    public ProgressReplication(Func<ServerSimulation?> simulation, Action<PlayerId, Message> send, ILogger<ProgressReplication>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(send);
        _simulation = simulation;
        _send = send;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the number of <see cref="PlayerProgressState"/> messages sent.</summary>
    public int StatesSent { get; private set; }

    /// <summary>Creates the replication for a runtime that has not started yet and registers it as a player observer; register it after the inventory service.</summary>
    /// <param name="runtime">The runtime.</param>
    /// <param name="loggers">Loggers, null for none.</param>
    /// <returns>The replication.</returns>
    public static ProgressReplication Register(ServerRuntime runtime, ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var replication = new ProgressReplication(
            () => runtime.RuntimeState is ServerRuntimeState.Running or ServerRuntimeState.SafeMode ? runtime.Simulation : null,
            (player, message) => runtime.Session.Send(player, message),
            loggers?.CreateLogger<ProgressReplication>());
        runtime.AddPlayerObserver(replication);
        return replication;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        if (!Attach())
        {
            return;
        }

        // Not synchronized yet: the discovery of the held items is announced by the state sent right after it.
        _players[player] = false;
        if (_attached!.DiscoverHeldItems(player) is { } applied)
        {
            _logger.HeldItemsDiscovered(player.Value, applied.Id.Value);
        }

        Send(player);
        _players[player] = true;
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => _players.Remove(player);

    private bool Attach()
    {
        if (_attached is not null)
        {
            return true;
        }

        if (_simulation() is not { } simulation)
        {
            return false;
        }

        simulation.Applied += OnApplied;
        _attached = simulation;
        return true;
    }

    private void OnApplied(AppliedOperation applied)
    {
        if (_players.Count == 0)
        {
            return;
        }

        OperationProgress.Collect(applied.Operation, _discoveredBy, out var keysChanged);
        if (!keysChanged && _discoveredBy.Count == 0)
        {
            return;
        }

        _targets.Clear();
        if (keysChanged)
        {
            foreach (var pair in _players)
            {
                _targets.Add(pair.Key);
            }
        }
        else
        {
            _targets.AddRange(_discoveredBy);
        }

        foreach (var player in _targets)
        {
            if (_players.TryGetValue(player, out var synced) && synced)
            {
                Send(player);
            }
        }
    }

    private void Send(PlayerId player)
    {
        var state = _attached!.State;
        var discovered = state.Progress.TryGet(player, out var progress) ? progress.DiscoveredItems : ItemTypeBitSet.Empty;
        _send(player, new PlayerProgressState(discovered, [.. state.ProgressKeys.Sorted], progress?.Food ?? FoodSlots.Empty));
        StatesSent++;
        _logger.ProgressSent(player.Value, discovered.Count, state.ProgressKeys.Count);
    }
}
