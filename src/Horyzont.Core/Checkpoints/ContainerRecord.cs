using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Checkpoints;

/// <summary>A container in a checkpoint (blob <c>Container</c>, 20).</summary>
/// <param name="Id">Container id.</param>
/// <param name="Kind">Kind.</param>
/// <param name="Host">Owner of the container.</param>
/// <param name="Owner">Owning player, <see cref="PlayerId.None"/> for none.</param>
/// <param name="Access">Access.</param>
/// <param name="Size">Number of slots, 1..256.</param>
/// <param name="Version">Version.</param>
/// <param name="Slots">Exactly <paramref name="Size"/> slots; empty slots are <see cref="ItemStack.Empty"/>.</param>
public sealed record ContainerRecord(
    ContainerId Id, ContainerKind Kind, ContainerHost Host, PlayerId Owner, ContainerAccess Access, ushort Size, uint Version,
    IReadOnlyList<ItemStack> Slots)
{
    /// <summary>Record of a live container.</summary>
    /// <param name="container">The container.</param>
    /// <returns>The record.</returns>
    public static ContainerRecord From(Container container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return new(
            container.Id, container.Kind, container.Host, container.Owner, container.Access, (ushort)container.Size, container.Version,
            container.Slots.ToArray());
    }
}
