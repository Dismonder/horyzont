using Horyzont.Core.Items;

namespace Horyzont.Core.Intents;

/// <summary>
/// Drop items from a slot of the player's own container (intent 21, spec 03). Until dropped items exist the server destroys them
/// (sink <see cref="ItemSink.Despawn"/>) and refuses tools (K6-29W D13).
/// </summary>
/// <param name="Slot">Slot.</param>
/// <param name="Count">Number of items, at least 1.</param>
public sealed record ItemDropIntent(SlotRef Slot, ushort Count) : Intent;
