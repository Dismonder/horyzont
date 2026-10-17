using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Client asks for the full state and the deltas of a container (message 42).</summary>
/// <param name="Container">Container.</param>
public sealed record ContainerSubscribe(ContainerId Container) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ContainerSubscribe;
}
