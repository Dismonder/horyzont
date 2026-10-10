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
    public ClientReplication(Action<Message> send, ConstructMirror? constructs, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(send);
        _send = send;
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
                break;
            case ConstructRemoved removed:
                Entities.Apply(message);
                Constructs?.Remove(removed.Construct);
                break;
            case ConstructState part:
                Constructs?.Apply(part);
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
    }

    private void Request(Core.Ids.EntityId entity) => _send(new ResyncRequest(entity));
}
