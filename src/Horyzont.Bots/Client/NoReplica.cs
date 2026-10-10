using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Bots.Client;

/// <summary>The replica of a bot that keeps nothing.</summary>
public sealed class NoReplica : IBotReplica
{
    /// <summary>Gets the shared instance.</summary>
    public static NoReplica Instance { get; } = new();

    /// <inheritdoc />
    public ulong Digest => 0;

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
    }

    /// <inheritdoc />
    public void Reset()
    {
    }

    /// <inheritdoc />
    public void WriteDump(TextWriter writer)
    {
    }
}
