using System.Numerics;

using Horyzont.Bots.Client;
using Horyzont.Bots.Digests;
using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Bots.Replicas;

/// <summary>
/// Entities and constructs of a bot through the client replication of K3-15 (<see cref="ClientReplication"/> with
/// <see cref="EntityMirror"/> and <see cref="ConstructMirror"/>): snapshots are rebuilt from their baselines and acknowledged, construct
/// states and operations update the mirror, gaps ask for a resync. Named apart from <c>Horyzont.Core.Replication.ConstructReplica</c>,
/// the single copy that the mirror holds per construct.
/// </summary>
/// <param name="time">Clock of the resync retries and transfer expiry.</param>
public sealed class ConstructMirrorReplica(TimeProvider time) : IBotReplica
{
    private ClientReplication? _replication;

    /// <summary>Raised for every operation the mirror handled (applied or not), with the time it arrived.</summary>
    public event Action<OpId>? OperationObserved;

    /// <inheritdoc />
    public ulong Digest => _replication?.Constructs is { } mirror ? ConstructDigest.Compute(mirror.Store.All) : 0;

    /// <inheritdoc />
    public bool IsReady => true;

    /// <inheritdoc />
    public ClientReplication? Replication => _replication;

    /// <summary>Gets the construct mirror; null before <see cref="Attach"/>.</summary>
    public ConstructMirror? Constructs => _replication?.Constructs;

    /// <inheritdoc />
    public void Attach(ClientSession session, IFullSnapshotSink snapshots)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(snapshots);
        var mirror = new ConstructMirror(time);
        mirror.OperationObserved += id => OperationObserved?.Invoke(id);
        _replication = new ClientReplication(session.Send, mirror, time);
        _replication.Decoder.AddSink(snapshots);
    }

    /// <inheritdoc />
    public void Start(ServerHello hello)
    {
    }

    /// <inheritdoc />
    public void OnMessage(Message message) => _replication?.OnMessage(message);

    /// <inheritdoc />
    public void OnFrame(PlanetId planet, Vector3 feet)
    {
        _replication?.Entities.RetryResyncs();
        _replication?.Constructs?.ExpireTransfers();
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => _replication?.OnDisconnected(code);

    /// <inheritdoc />
    public void WriteDump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (_replication?.Constructs is { } mirror)
        {
            DigestDump.Write(writer, mirror.Store.All.Select(ConstructDigest.Entry));
        }
    }
}
