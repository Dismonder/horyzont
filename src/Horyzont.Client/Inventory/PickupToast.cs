using Horyzont.Core.Ids;

namespace Horyzont.Client.Inventory;

/// <summary>One notification on the right of the screen.</summary>
/// <param name="Kind">What it says.</param>
/// <param name="Type">Item type; none for <see cref="PickupToastKind.InventoryFull"/>.</param>
/// <param name="Amount">Number of items gained within the aggregation window.</param>
/// <param name="CreatedMs">Time the toast appeared (milliseconds, any monotonic origin).</param>
/// <param name="ExpiresMs">Time the toast disappears.</param>
public readonly record struct PickupToast(PickupToastKind Kind, ItemTypeId Type, int Amount, long CreatedMs, long ExpiresMs);
