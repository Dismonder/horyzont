using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Bots.Client;

/// <summary>
/// A part of the world that a bot replicates from the server messages (constructs, terrain versions). Phase A had none
/// (<see cref="NoReplica"/>); later phases attach replicas without changing the loop of the bot. The bot calls <see cref="Attach"/>
/// once, <see cref="Start"/> on every server hello before it says ready, <see cref="OnFrame"/> every tick while connected and
/// <see cref="OnDisconnected"/> when a connection ends.
/// </summary>
public interface IBotReplica
{
    /// <summary>Gets a digest of the replicated state; 0 when nothing is replicated.</summary>
    ulong Digest { get; }

    /// <summary>Gets a value indicating whether the replicated world is ready (the startup transfer of the terrain is complete).</summary>
    bool IsReady { get; }

    /// <summary>
    /// Gets the replication router when the replica decodes snapshots and mirrors entities and constructs; null when the bot reads
    /// the raw snapshots itself.
    /// </summary>
    ClientReplication? Replication { get; }

    /// <summary>Connects the replica to the session of the bot and to the receiver of full snapshots (the own state of the bot).</summary>
    /// <param name="session">The session; replies (acknowledgements, reports, views) go through it.</param>
    /// <param name="snapshots">Receives every full snapshot.</param>
    void Attach(ClientSession session, IFullSnapshotSink snapshots);

    /// <summary>Starts a session for a server hello: reports what is cached before the bot says ready.</summary>
    /// <param name="hello">The hello.</param>
    void Start(ServerHello hello);

    /// <summary>Offers a server message to the replica.</summary>
    /// <param name="message">The message.</param>
    void OnMessage(Message message);

    /// <summary>One tick of a connected bot: views, retries, expiries.</summary>
    /// <param name="planet">Planet of the bot; default before the first own state.</param>
    /// <param name="feet">Feet of the latest own state of the server.</param>
    void OnFrame(PlanetId planet, Vector3 feet);

    /// <summary>The connection ended; the next one starts from a full state (persistent caches stay).</summary>
    /// <param name="code">Why.</param>
    void OnDisconnected(DisconnectCode code);

    /// <summary>Writes the entries of the replica as JSON lines, sorted, for the comparison with the server.</summary>
    /// <param name="writer">Destination.</param>
    void WriteDump(TextWriter writer);
}
