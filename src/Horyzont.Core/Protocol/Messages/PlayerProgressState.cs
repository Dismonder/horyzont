using Horyzont.Core.Progress;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The discoveries of the receiving player and the progress keys of the world (message 48, K6-32 D11); reliable ordered, server to the owner only.
/// Format 1: the bitset (varuint length 0 to 513, canonical bytes), a varuint count of keys (0 to 256) and the keys in ascending ordinal order; a later
/// package extends it by appending fields and raising the protocol version (K6-32 D27).
/// </summary>
/// <param name="Discovered">Item types the player discovered.</param>
/// <param name="WorldProgressKeys">Progress keys of the world, ascending by <see cref="string.CompareOrdinal(string, string)"/>, at most <c>ProtocolConstants.MaxProgressKeys</c>.</param>
public sealed record PlayerProgressState(ItemTypeBitSet Discovered, IReadOnlyList<string> WorldProgressKeys) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.PlayerProgressState;
}
