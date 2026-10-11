using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Markers;
using Horyzont.Server.Replication;

namespace Horyzont.Server.Markers;

/// <summary>
/// Sends map markers to players (K5-27 decision 9) and is the only sender of marker operations: <see cref="MarkersState"/> (every
/// shared marker and the player's own private markers) when a player becomes ready, then <see cref="OperationApplied"/> for every
/// later marker operation, shared ones to all synchronized players and private ones to the owner only. The general entity replication
/// forwards only operations with construct changes, so it never sends operations 22 and 23. A private marker of another player never
/// reaches a player: not in the state, not as an operation.
/// An operation applied while a player waits for the state (the budget had no tokens) is not queued: the state is built from the
/// world when it is finally sent, so it already contains the operation. Main thread only.
/// </summary>
public sealed class MarkerReplication : IServerPlayerObserver, IServerTickParticipant
{
    private readonly Func<ServerSimulation?> _simulation;
    private readonly Action<PlayerId, Message> _send;
    private readonly Func<IReplicationSendBudget?> _budget;
    private readonly Dictionary<PlayerId, bool> _players = [];
    private readonly Dictionary<MarkerId, PlayerId?> _owners = [];
    private readonly List<PlayerId> _waiting = [];
    private readonly List<MarkerRecord> _visible = [];
    private readonly ByteWriter _sizer = new(4096);
    private ServerSimulation? _attached;

    /// <summary>Initializes a new instance of the <see cref="MarkerReplication"/> class.</summary>
    /// <param name="simulation">The simulation of the running world (null before the world has started).</param>
    /// <param name="send">Sends a message to one player.</param>
    /// <param name="budget">The shared reliable send budget (null: unlimited).</param>
    public MarkerReplication(Func<ServerSimulation?> simulation, Action<PlayerId, Message> send, Func<IReplicationSendBudget?>? budget = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(send);
        _simulation = simulation;
        _send = send;
        _budget = budget ?? (() => null);
    }

    /// <summary>Gets the number of <see cref="MarkersState"/> messages sent.</summary>
    public int StatesSent { get; private set; }

    /// <summary>Gets the number of marker operations sent (one per recipient).</summary>
    public int OperationsSent { get; private set; }

    /// <summary>Creates the replication for a runtime that has not started yet and registers it as a player observer and tick participant.</summary>
    /// <param name="runtime">The runtime.</param>
    /// <param name="budget">The shared reliable send budget of the world (null: unlimited).</param>
    /// <returns>The replication.</returns>
    public static MarkerReplication Register(ServerRuntime runtime, Func<IReplicationSendBudget?>? budget = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var replication = new MarkerReplication(
            () => runtime.RuntimeState == ServerRuntimeState.Running || runtime.RuntimeState == ServerRuntimeState.SafeMode ? runtime.Simulation : null,
            (player, message) => runtime.Session.Send(player, message),
            budget);
        runtime.AddTickParticipant(replication);
        runtime.AddPlayerObserver(replication);
        return replication;
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        if (!Attach())
        {
            return;
        }

        for (var i = _waiting.Count - 1; i >= 0; i--)
        {
            if (TrySendState(_waiting[i]))
            {
                _waiting.RemoveAt(i);
            }
        }
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        if (!Attach())
        {
            return;
        }

        _players[player] = false;
        if (!TrySendState(player))
        {
            _waiting.Add(player);
        }
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
    {
        _players.Remove(player);
        _waiting.Remove(player);
    }

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

        foreach (var marker in simulation.State.Markers.All)
        {
            _owners[marker.Id] = marker.Owner;
        }

        simulation.Applied += OnApplied;
        _attached = simulation;
        return true;
    }

    private bool TrySendState(PlayerId player)
    {
        var state = _attached!.State;
        _visible.Clear();
        foreach (var marker in state.Markers.All)
        {
            if (marker.Owner is null || marker.Owner == player)
            {
                _visible.Add(marker);
            }
        }

        var message = new MarkersState(_visible.ToArray());
        if (_budget() is { } budget)
        {
            _sizer.Reset();
            MessageCodec.Write(_sizer, message);
            if (!budget.TryConsume(player, ReplicationTraffic.Bulk, _sizer.Length))
            {
                return false;
            }
        }

        _send(player, message);
        _players[player] = true;
        StatesSent++;
        return true;
    }

    private void OnApplied(AppliedOperation applied)
    {
        switch (applied.Operation)
        {
            case MarkerSetOp set:
                _owners[set.Marker] = set.Owner;
                Forward(applied, set.Owner);
                break;
            case MarkerRemoveOp remove:
                if (_owners.Remove(remove.Marker, out var owner))
                {
                    Forward(applied, owner);
                }

                break;
        }
    }

    private void Forward(AppliedOperation applied, PlayerId? owner)
    {
        if (_players.Count == 0)
        {
            return;
        }

        var message = new OperationApplied(applied.Id, applied.Tick, applied.Operation, []);
        var budget = _budget();
        if (owner is { } only)
        {
            SendLive(only, message, budget);
            return;
        }

        foreach (var pair in _players)
        {
            SendLive(pair.Key, message, budget);
        }
    }

    private void SendLive(PlayerId player, OperationApplied message, IReplicationSendBudget? budget)
    {
        if (!_players.TryGetValue(player, out var synced) || !synced)
        {
            return;
        }

        if (budget is not null)
        {
            _sizer.Reset();
            MessageCodec.Write(_sizer, message);
            budget.TryConsume(player, ReplicationTraffic.Live, _sizer.Length);
        }

        _send(player, message);
        OperationsSent++;
    }
}
