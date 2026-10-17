using Horyzont.Core.Items;

namespace Horyzont.Core.Content;

/// <summary>One item of the start kit.</summary>
/// <param name="ItemKey">Item key.</param>
/// <param name="Count">Number of items, 1..stack limit.</param>
/// <param name="Container">Player container: <see cref="ContainerKind.PlayerBag"/>, <see cref="ContainerKind.Hotbar"/> or
/// <see cref="ContainerKind.Equipment"/>.</param>
/// <param name="Slot">Slot in the container.</param>
public sealed record StartKitEntry(string ItemKey, ushort Count, ContainerKind Container, ushort Slot);
