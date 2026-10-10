using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Bots.Client;

/// <summary>
/// A part of the world that a bot replicates from the server messages (constructs, terrain versions). Phase A has none
/// (<see cref="NoReplica"/>); later phases attach replicas without changing the loop of the bot.
/// </summary>
public interface IBotReplica
{
    /// <summary>Gets a digest of the replicated state; 0 when nothing is replicated.</summary>
    ulong Digest { get; }

    /// <summary>Offers a server message to the replica.</summary>
    /// <param name="message">The message.</param>
    void OnMessage(Message message);

    /// <summary>Forgets everything: the connection ended and the next one starts from a full state.</summary>
    void Reset();

    /// <summary>Writes the entries of the replica as JSON lines, sorted, for the comparison with the server.</summary>
    /// <param name="writer">Destination.</param>
    void WriteDump(TextWriter writer);
}
