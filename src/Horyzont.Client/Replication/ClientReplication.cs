using Horyzont.Client.Session;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Replication;

/// <summary>
/// The one listener that routes replication messages on a client (K3-15): snapshots through the baseline decoder (acknowledged only
/// after reconstruction), lifecycle messages to the entity mirror, construct states and operations to the construct mirror, and
/// repair requests back to the server. Hosts the local client without a construct mirror, because the host shares its world.
/// </summary>
public sealed class ClientReplication : IClientSessionListener
{
    private readonly Action<Message> _send;

    /// <summary>Initializes a new instance of the <see cref="ClientReplication"/> class.</summary>
    /// <param name="send">Sends a client message to the server (reliable for resync, unreliable for acknowledgements).</param>
    /// <param name="constructs">Mirror of constructs; null when the world is shared with the server.</param>
    /// <param name="time">Clock of the resync retries; the system clock when omitted.</param>
    /// <param name="blockEntities">The states of the functional blocks (message 46, K6-30); null when the client does not track them.</param>
    /// <param name="progress">The discoveries and progress keys (message 48, K6-32); null when the client does not track them.</param>
    public ClientReplication(
        Action<Message> send, ConstructMirror? constructs, TimeProvider? time = null, Blocks.ClientBlockEntities? blockEntities = null,
        Crafting.ClientProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(send);
        _send = send;
        Progress = progress;
        BlockEntities = blockEntities;
        Constructs = constructs;
        Entities = new EntityMirror(time);
        Decoder = new SnapshotDecoder(id => _send(new SnapshotAck(id)));
        Decoder.AddSink(Entities);
        Entities.ResyncNeeded += Request;
        if (constructs is not null)
        {
            constructs.ResyncNeeded += Request;
        }
    }

    /// <summary>Gets the decoder; register the consumers of full snapshots with <see cref="SnapshotDecoder.AddSink"/>.</summary>
    public SnapshotDecoder Decoder { get; }

    /// <summary>Gets the entity mirror.</summary>
    public EntityMirror Entities { get; }

    /// <summary>Gets the construct mirror; null for a client that shares the server world.</summary>
    public ConstructMirror? Constructs { get; }

    /// <summary>Gets the states of the functional blocks; null when not tracked.</summary>
    public Blocks.ClientBlockEntities? BlockEntities { get; }

    /// <summary>Gets the discoveries and progress keys; null when not tracked.</summary>
    public Crafting.ClientProgress? Progress { get; }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        switch (message)
        {
            case Snapshot snapshot:
                Decoder.OnMessage(snapshot);
                break;
            case EntitySpawn:
                Entities.Apply(message);
                break;
            case EntityDespawn despawn:
                Entities.Apply(message);
                Constructs?.Remove(despawn.Entity);
                BlockEntities?.Remove(despawn.Entity);
                break;
            case ConstructRemoved removed:
                Entities.Apply(message);
                Constructs?.Remove(removed.Construct);
                BlockEntities?.Remove(removed.Construct);
                break;
            case ConstructState part:
                if (part.PartIndex == 0)
                {
                    // A new full state of a construct starts: its block states follow it (K6-30 D15).
                    BlockEntities?.Remove(part.Header.Id);
                }

                Constructs?.Apply(part);
                break;
            case BlockEntityStates states:
                BlockEntities?.Apply(states);
                break;
            case OperationApplied operation:
                Entities.Apply(operation);
                Constructs?.Apply(operation);
                break;
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        Decoder.OnDisconnected(code);
        Entities.Clear();
        Constructs?.Clear();
        BlockEntities?.Clear();
    }

    private void Request(Core.Ids.EntityId entity) => _send(new ResyncRequest(entity));
}
