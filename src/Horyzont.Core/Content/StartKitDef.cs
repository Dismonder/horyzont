namespace Horyzont.Core.Content;

/// <summary>Player containers and the start kit of a new player (<c>content/start.json</c>).</summary>
/// <param name="BagSize">Slots of the bag.</param>
/// <param name="HotbarSize">Slots of the hotbar.</param>
/// <param name="EquipmentSize">Slots of the equipment.</param>
/// <param name="Entries">Items, in file order.</param>
public sealed record StartKitDef(ushort BagSize, ushort HotbarSize, ushort EquipmentSize, IReadOnlyList<StartKitEntry> Entries);
