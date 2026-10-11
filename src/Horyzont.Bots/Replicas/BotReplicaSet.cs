using System.Numerics;

using Horyzont.Bots.Client;
using Horyzont.Bots.Digests;
using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Bots.Replicas;

/// <summary>The full replica of a phase B bot: constructs (and the entities and snapshots with them) plus terrain.</summary>
/// <param name="constructs">The construct mirror replica.</param>
/// <param name="terrain">The terrain replica.</param>
public sealed class BotReplicaSet(ConstructMirrorReplica constructs, TerrainReplica terrain) : IBotReplica, IDisposable
{
    /// <summary>Gets the construct part.</summary>
    public ConstructMirrorReplica Constructs { get; } = constructs ?? throw new ArgumentNullException(nameof(constructs));

    /// <summary>Gets the terrain part.</summary>
    public TerrainReplica Terrain { get; } = terrain ?? throw new ArgumentNullException(nameof(terrain));

    /// <inheritdoc />
    public ulong Digest => DigestDump.Combine(Entries());

    /// <inheritdoc />
    public bool IsReady => Terrain.IsReady;

    /// <inheritdoc />
    public ClientReplication? Replication => Constructs.Replication;

    /// <inheritdoc />
    public void Attach(ClientSession session, IFullSnapshotSink snapshots)
    {
        Constructs.Attach(session, snapshots);
        Terrain.Attach(session, snapshots);
    }

    /// <inheritdoc />
    public void Start(ServerHello hello)
    {
        Constructs.Start(hello);
        Terrain.Start(hello);
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        Constructs.OnMessage(message);
        Terrain.OnMessage(message);
    }

    /// <inheritdoc />
    public void OnFrame(PlanetId planet, Vector3 feet)
    {
        Constructs.OnFrame(planet, feet);
        Terrain.OnFrame(planet, feet);
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        Constructs.OnDisconnected(code);
        Terrain.OnDisconnected(code);
    }

    /// <inheritdoc />
    public void WriteDump(TextWriter writer) => DigestDump.Write(writer, Entries());

    /// <inheritdoc />
    public void Dispose() => Terrain.Dispose();

    /// <summary>Gets the entries of both parts.</summary>
    /// <returns>Construct and terrain entries.</returns>
    public IReadOnlyList<DigestEntry> Entries()
    {
        var entries = new List<DigestEntry>();
        if (Constructs.Constructs is { } mirror)
        {
            entries.AddRange(mirror.Store.All.Select(ConstructDigest.Entry));
        }

        entries.AddRange(Terrain.Held().Select(e => TerrainVersionDigest.Entry(e.Key, e.Version, e.Hash)));
        return entries;
    }
}
