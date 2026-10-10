using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Net.Session;

namespace Horyzont.Game.Server.Characters;

/// <summary>
/// The bridge to K3-15 (which replaces it with full snapshot building): every third tick (20 Hz) each player in the game gets a
/// full <see cref="Snapshot"/> with only its own character, so the client can acknowledge inputs and correct its prediction. The
/// name and the call after <c>ServerCharacters.Step</c> are a contract for K3-15.
/// </summary>
public sealed class OwnStateSender
{
    /// <summary>Ticks between two snapshots of one player.</summary>
    public const int SendEveryTicks = 3;

    private const EntityStateFields OwnFields =
        EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.Velocity | EntityStateFields.MotionFlags;

    private readonly ServerSession _session;
    private readonly ServerSimulation _simulation;
    private readonly IServerCharacterSource _characters;
    private readonly Dictionary<PlayerId, uint> _lastSnapshotId = [];
    private readonly SnapshotFrame[] _frames = new SnapshotFrame[1];
    private readonly EntityState[] _entities = new EntityState[1];
    private readonly Snapshot _template;
    private readonly ByteWriter _body = new(256);
    private readonly ByteWriter _header = new(64);
    private readonly byte[] _encoded = new byte[ProtocolConstants.MaxUnreliableMessageBytes];

    /// <summary>Initializes a new instance of the <see cref="OwnStateSender"/> class.</summary>
    /// <param name="session">The session the snapshots go through.</param>
    /// <param name="simulation">The simulation (entity ids of the players).</param>
    /// <param name="characters">The bodies whose state is sent.</param>
    public OwnStateSender(ServerSession session, ServerSimulation simulation, IServerCharacterSource characters)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(characters);
        _session = session;
        _simulation = simulation;
        _characters = characters;
        _template = new Snapshot(0, new Tick(0), 0, 0, _frames, _entities);
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
        _frames[0] = new SnapshotFrame(state.Frame, state.ParentSeq);
        _entities[0] = entity;
        _session.SendEncoded(player, MessageType.Snapshot, Encode(id, tick, state.LastProcessedInputSeq));
    }

    private ReadOnlySpan<byte> Encode(uint id, Tick tick, uint lastInput)
    {
        // The private template owns these arrays. MessageCodec consumes them synchronously and supplies all canonical frame,
        // entity and quantization bytes. Replace only the four scalar header fields of this full snapshot; no codec changes.
        _body.Reset();
        MessageCodec.Write(_body, _template);
        var bytes = _body.WrittenSpan;
        var reader = new ByteReader(bytes);
        _header.Reset();
        _header.WriteByte(reader.ReadByte());
        _header.WriteByte(reader.ReadByte());
        for (var i = 0; i < 4; i++)
        {
            _ = reader.ReadVarUInt();
        }

        _header.WriteVarUInt(id);
        _header.WriteVarUInt(tick.Value);
        _header.WriteVarUInt(0);
        _header.WriteVarUInt(lastInput);
        var payload = bytes[reader.Position..];
        var header = _header.WrittenSpan;
        header.CopyTo(_encoded);
        payload.CopyTo(_encoded.AsSpan(header.Length));
        // Borrowed until SendEncoded returns; no session, transport or caller may retain this mutable buffer.
        return _encoded.AsSpan(0, header.Length + payload.Length);
    }
}
