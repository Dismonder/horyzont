using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Intents;

/// <summary>
/// Move, split, merge or swap items between slots of the player's own containers (intent 20, spec 03). The expected type and count are
/// what the client saw in the source slot; a different source content is rejected with <see cref="RejectReason.ContainerChanged"/>.
/// </summary>
/// <param name="From">Source slot.</param>
/// <param name="To">Target slot or any slot of a container.</param>
/// <param name="Count">Number of items to move, at least 1.</param>
/// <param name="ExpectedType">Type the client saw in the source slot.</param>
/// <param name="ExpectedCount">Count the client saw in the source slot.</param>
public sealed record ItemMoveIntent(SlotRef From, ItemMoveTarget To, ushort Count, ItemTypeId ExpectedType, ushort ExpectedCount) : Intent;
