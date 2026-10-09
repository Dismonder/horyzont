using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Intents;

/// <summary>
/// Target of an <see cref="ItemMoveIntent"/>: one slot, or any slot of a container (the server fills stacks first, then empty slots).
/// Create it with <see cref="ToSlot"/> or <see cref="AnyIn"/>.
/// </summary>
/// <param name="Container">Target container.</param>
/// <param name="Slot">Target slot; 0 when <paramref name="AnySlot"/> is true.</param>
/// <param name="AnySlot">True for any slot of <paramref name="Container"/>.</param>
public readonly record struct ItemMoveTarget(ContainerId Container, ushort Slot, bool AnySlot)
{
    /// <summary>The slot as a reference; meaningful only when <see cref="AnySlot"/> is false.</summary>
    public SlotRef SlotRef => new(Container, Slot);

    /// <summary>One slot.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The target.</returns>
    public static ItemMoveTarget ToSlot(SlotRef slot) => new(slot.Container, slot.Slot, false);

    /// <summary>Any slot of a container.</summary>
    /// <param name="container">The container.</param>
    /// <returns>The target.</returns>
    public static ItemMoveTarget AnyIn(ContainerId container) => new(container, 0, true);
}
