using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>A construct no longer exists.</summary>
/// <param name="Construct">Construct.</param>
public sealed record ConstructRemoved(EntityId Construct) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ConstructRemoved;
}
