using Horyzont.Core.Items;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Content of one slot in <see cref="ContainerState"/> or <see cref="ContainerDelta"/>; an empty stack clears the slot.</summary>
/// <param name="Slot">Slot index.</param>
/// <param name="Stack">The stack; <see cref="ItemStack.Empty"/> for an empty slot (type 0 on the wire).</param>
public readonly record struct SlotContent(ushort Slot, ItemStack Stack);
