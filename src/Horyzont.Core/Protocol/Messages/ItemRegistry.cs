using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>The world registry of item types (message 49), sent before any container state.</summary>
/// <param name="Entries">Entries, ids 1..4096, at most 4096 of them.</param>
public sealed record ItemRegistry(IReadOnlyList<RegistryEntry> Entries) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ItemRegistry;
}
