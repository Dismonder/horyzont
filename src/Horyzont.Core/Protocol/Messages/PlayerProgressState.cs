using Horyzont.Core.Progress;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The discoveries of the receiving player and the progress keys of the world (message 48, K6-32 D11); reliable ordered, server to the owner only.
/// The enum member <c>MessageType.PlayerProgressState</c> and the codec arrive with step B2, so the type is the number 48 until then.
/// </summary>
/// <param name="Discovered">Item types the player discovered.</param>
/// <param name="WorldProgressKeys">Progress keys of the world, ascending by <see cref="string.CompareOrdinal(string, string)"/>, at most <c>ProtocolConstants.MaxProgressKeys</c>.</param>
public sealed record PlayerProgressState(ItemTypeBitSet Discovered, IReadOnlyList<string> WorldProgressKeys) : Message
{
    /// <inheritdoc />
    public override MessageType Type => (MessageType)48;
}
