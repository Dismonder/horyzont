using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Intents;

/// <summary>
/// Drop items from a slot of the player's own container (intent 21, spec 03). Until dropped items exist the server destroys them
/// (sink <see cref="ItemSink.Despawn"/>) and refuses tools (K6-29W D13). The expected type and count are what the client saw in the
/// slot; a different content is rejected with <see cref="RejectReason.ContainerChanged"/>.
/// </summary>
/// <param name="Slot">Slot.</param>
/// <param name="Count">Number of items, at least 1.</param>
/// <param name="ExpectedType">Type the client saw in the slot.</param>
/// <param name="ExpectedCount">Count the client saw in the slot.</param>
public sealed record ItemDropIntent(SlotRef Slot, ushort Count, ItemTypeId ExpectedType, ushort ExpectedCount) : Intent;
