using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The full list of functional block states of one construct (message 46, K6-30 D15); an empty list clears the client's map of the
/// construct. At most <see cref="ProtocolConstants.MaxBlockEntitiesPerConstruct"/> entries.
/// </summary>
/// <param name="Construct">Construct.</param>
/// <param name="Entries">States of the blocks.</param>
public sealed record BlockEntityStates(EntityId Construct, IReadOnlyList<BlockEntityEntry> Entries) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.BlockEntityStates;
}
