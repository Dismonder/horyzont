using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Requests a fresh state of a visible entity; accepted only in game.</summary>
/// <param name="Entity">Nonzero entity identifier.</param>
public sealed record ResyncRequest(EntityId Entity) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ResyncRequest;
}
