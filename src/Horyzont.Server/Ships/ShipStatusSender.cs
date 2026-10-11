using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Net.Session;

namespace Horyzont.Server.Ships;

/// <summary>
/// Sends <c>ShipStatus</c> (31) at 2 Hz (K5-26 decision 9): every <see cref="IntervalTicks"/> ticks, in phase Compose, to each
/// player in the game whose own entity is in the frame of a ship-mode construct (seated or attached) with a known flight status.
/// Unreliable and outside the replication budgets (control data). Encodes into one reused buffer, so it does not allocate. Main thread.
/// </summary>
/// <param name="source">The flight status; null before the flight exists (no status is sent).</param>
public sealed class ShipStatusSender(Func<IShipStatusSource?> source) : IServerTickParticipant
{
    /// <summary>Ticks between two statuses (2 Hz at 60 ticks per second).</summary>
    public const int IntervalTicks = 30;

    private readonly Func<IShipStatusSource?> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly byte[] _frame = new byte[MessageCodec.ShipStatusFrameBytes];

    /// <inheritdoc />
    public ServerTickPhase Phase => ServerTickPhase.Compose;

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (runtime.State.CurrentTick.Value % IntervalTicks == 0)
        {
            Send(runtime.Session, runtime.State);
        }
    }

    /// <summary>Sends the statuses of the current tick now.</summary>
    /// <param name="session">The server session.</param>
    /// <param name="world">The world.</param>
    /// <returns>The number of statuses sent.</returns>
    public int Send(ServerSession session, WorldState world)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(world);
        if (_source() is not { } flight)
        {
            return 0;
        }

        var sent = 0;
        var players = session.PlayersInGame;
        for (var i = 0; i < players.Count; i++)
        {
            if (!world.Players.TryGet(players[i], out var player) || player.Frame.Kind != FrameKind.Construct)
            {
                continue;
            }

            var ship = new EntityId(player.Frame.Id);
            if (!world.Constructs.TryGet(ship, out var construct) || construct.Mode != ConstructMode.Ship
                || !flight.TryGetStatus(ship, out var status))
            {
                continue;
            }

            MessageCodec.EncodeShipStatus(_frame, ship, world.CurrentTick, in status);
            session.SendEncoded(players[i], MessageType.ShipStatus, _frame);
            sent++;
        }

        return sent;
    }
}
