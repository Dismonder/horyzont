namespace Horyzont.Core.Content;

/// <summary>An item type of the game content (<c>content/items/</c>). Visual fields are read only by the game.</summary>
/// <param name="Key">Content key, e.g. <c>core:stone</c>; binds the type to the world registry.</param>
/// <param name="Category">Category.</param>
/// <param name="MaxStack">Stack limit, 1..999.</param>
/// <param name="Flags">Flags.</param>
/// <param name="ToolTier">Tool tier, 0 for none.</param>
/// <param name="EquipmentSlot">Equipment slot.</param>
/// <param name="Food">Food statistics (K6-33), null for an item that is not food.</param>
public sealed record ItemDef(string Key, ItemCategory Category, ushort MaxStack, ItemFlags Flags, byte ToolTier, EquipmentSlot EquipmentSlot, FoodDef? Food = null);
