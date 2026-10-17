using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// Full state of a subscribed container (message 40), or a refused subscription: with <paramref name="Reason"/> other than
/// <see cref="RejectReason.None"/> the remaining fields are default and not encoded (use <see cref="Rejected"/>).
/// </summary>
/// <param name="Container">Container.</param>
/// <param name="Reason">Why the subscription was refused; <see cref="RejectReason.None"/> for a state.</param>
/// <param name="Kind">Container kind.</param>
/// <param name="Access">Access.</param>
/// <param name="Size">Number of slots, 1..256.</param>
/// <param name="Version">Container version.</param>
/// <param name="Slots">Slots listed in strictly ascending order, each below <paramref name="Size"/>; unlisted slots are empty.</param>
public sealed record ContainerState(
    ContainerId Container, RejectReason Reason, ContainerKind Kind, ContainerAccess Access, ushort Size, uint Version,
    IReadOnlyList<SlotContent> Slots) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ContainerState;

    /// <summary>A refused subscription.</summary>
    /// <param name="container">Container.</param>
    /// <param name="reason">Reason, not <see cref="RejectReason.None"/>.</param>
    /// <returns>The message.</returns>
    public static ContainerState Rejected(ContainerId container, RejectReason reason) => new(container, reason, default, default, 0, 0, []);
}
