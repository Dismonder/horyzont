using Horyzont.Core.Ids;

namespace Horyzont.Core.Items;

/// <summary>A slot of a container.</summary>
/// <param name="Container">Container.</param>
/// <param name="Slot">Slot index from 0.</param>
public readonly record struct SlotRef(ContainerId Container, ushort Slot);
