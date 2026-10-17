using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Client no longer wants the deltas of a container (message 43); ignored for the player's own containers.</summary>
/// <param name="Container">Container.</param>
public sealed record ContainerUnsubscribe(ContainerId Container) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ContainerUnsubscribe;
}
