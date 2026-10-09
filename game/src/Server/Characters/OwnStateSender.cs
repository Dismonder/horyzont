using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Net.Session;

namespace Horyzont.Game.Server.Characters;

/// <summary>
/// The bridge to K3-15 (which replaces it with full snapshot building): every third tick (20 Hz) each player in the game gets a
/// full <see cref="Snapshot"/> with only its own character, so the client can acknowledge inputs and correct its prediction. The
/// name and the call after <see cref="ServerCharacters.Step"/> are a contract for K3-15.
/// </summary>
public sealed class OwnStateSender
{
    /// <summary>Ticks between two snapshots of one player.</summary>
    public const int SendEveryTicks = 3;

    private const EntityStateFields OwnFields =
        EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.Velocity | EntityStateFields.MotionFlags;

    private readonly ServerSession _session;
    private readonly ServerSimulation _simulation;
    private readonly ServerCharacters _characters;
    private readonly Dictionary<PlayerId, uint> _lastSnapshotId = [];

    /// <summary>Initializes a new instance of the <see cref="OwnStateSender"/> class.</summary>
    /// <param name="session">The session the snapshots go through.</param>
    /// <param name="simulation">The simulation (entity ids of the players).</param>
    /// <param name="characters">The bodies whose state is sent.</param>
    public OwnStateSender(ServerSession session, ServerSimulation simulation, ServerCharacters characters)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(characters);
        _session = session;
        _simulation = simulation;
        _characters = characters;
    }

    /// <summary>Gets the number of players whose snapshot counter is kept.</summary>
    public int TrackedPlayers => _lastSnapshotId.Count;

    /// <summary>Drops the snapshot counter of a player who left, so a rejoin starts a new sequence and the table does not grow.</summary>
    /// <param name="player">The player.</param>
    public void Forget(PlayerId player) => _lastSnapshotId.Remove(player);

    /// <summary>Sends the snapshots due at this tick.</summary>
    /// <param name="tick">The current tick of the simulation.</param>
    public void Send(Tick tick)
    {
        if (tick.Value % SendEveryTicks != 0)
        {
            return;
        }

        var players = _session.PlayersInGame;
        for (var i = 0; i < players.Count; i++)
        {
            SendTo(players[i], tick);
        }
    }

    private void SendTo(PlayerId player, Tick tick)
    {
        if (!_characters.TryGetState(player, out var state) || !_simulation.State.Players.TryGet(player, out var simulated))
        {
            return;
        }

        _lastSnapshotId.TryGetValue(player, out var previous);
        var id = previous + 1;
        _lastSnapshotId[player] = id;
        var entity = new EntityState(
            simulated.Entity, OwnFields, 0, state.ParentSeq, state.Feet, System.Numerics.Quaternion.Normalize(state.BodyRotation), state.Velocity,
            (byte)state.Mode);
        _session.Send(player, new Snapshot(id, tick, 0, state.LastProcessedInputSeq, [new SnapshotFrame(state.Frame, state.ParentSeq)], [entity]));
    }
}
