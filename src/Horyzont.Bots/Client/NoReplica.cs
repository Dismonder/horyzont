using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Bots.Client;

/// <summary>The replica of a bot that keeps nothing: raw snapshots go straight to the bot (servers without entity replication).</summary>
public sealed class NoReplica : IBotReplica
{
    private IFullSnapshotSink? _snapshots;

    /// <inheritdoc />
    public ulong Digest => 0;

    /// <inheritdoc />
    public bool IsReady => true;

    /// <inheritdoc />
    public ClientReplication? Replication => null;

    /// <inheritdoc />
    public void Attach(ClientSession session, IFullSnapshotSink snapshots) => _snapshots = snapshots;

    /// <inheritdoc />
    public void Start(ServerHello hello)
    {
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        if (message is Snapshot snapshot)
        {
            _snapshots?.OnFullSnapshot(snapshot);
        }
    }

    /// <inheritdoc />
    public void OnFrame(PlanetId planet, Vector3 feet)
    {
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
    }

    /// <inheritdoc />
    public void WriteDump(TextWriter writer)
    {
    }
}
