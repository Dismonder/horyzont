using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;

namespace Horyzont.Client.Ships;

/// <summary>
/// Flight status of ships from <c>ShipStatus</c> (31, K5-26 decision 10): the newest status by server tick per ship; one older than
/// <see cref="StaleAfter"/> (by local receive time) is unknown, so the HUD shows dashes. Forgotten when the ship despawns and when
/// the session ends. Main thread.
/// </summary>
/// <param name="time">Clock of the receive times.</param>
public sealed class ClientShipStatus(TimeProvider? time = null) : IClientShipStatus, IClientSessionListener
{
    /// <summary>Age after which a status is unknown.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<EntityId, Entry> _ships = [];

    /// <inheritdoc />
    public bool TryGet(EntityId ship, out ShipFlightStatus status)
    {
        if (_ships.TryGetValue(ship, out var entry) && _time.GetElapsedTime(entry.ReceivedAt) <= StaleAfter)
        {
            status = entry.Status;
            return true;
        }

        status = default;
        return false;
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        switch (message)
        {
            case ShipStatus update:
                if (!_ships.TryGetValue(update.Ship, out var known) || update.Tick.Value > known.Tick.Value)
                {
                    _ships[update.Ship] = new Entry(
                        update.Tick,
                        new ShipFlightStatus(update.Fuel, update.FuelCapacity, update.SpeedLimit, update.Flags, update.LaunchBlock),
                        _time.GetTimestamp());
                }

                break;
            case EntityDespawn despawn:
                _ships.Remove(despawn.Entity);
                break;
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => _ships.Clear();

    private readonly record struct Entry(Tick Tick, ShipFlightStatus Status, long ReceivedAt);
}
