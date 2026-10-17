using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Changed slots of a subscribed container after one journal operation (message 41); applies only to version − 1.</summary>
/// <param name="Container">Container.</param>
/// <param name="Version">Container version after the change.</param>
/// <param name="Changed">Changed slots in strictly ascending order, at most 256.</param>
public sealed record ContainerDelta(ContainerId Container, uint Version, IReadOnlyList<SlotContent> Changed) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ContainerDelta;
}
