using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>An entity disappears for the client.</summary>
/// <param name="Entity">Entity.</param>
public sealed record EntityDespawn(EntityId Entity) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.EntityDespawn;
}
