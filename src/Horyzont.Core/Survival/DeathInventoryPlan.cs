using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>One stack that a death moves to a tombstone: the source slot, the item, the count and the empty slot of the tombstone it goes to.</summary>
/// <param name="From">The source slot in a container of the player.</param>
/// <param name="Type">Item type of the stack.</param>
/// <param name="Count">Number of items; the whole stack.</param>
/// <param name="TargetSlot">Slot of the tombstone container; one stack per slot, never merged.</param>
public readonly record struct DeathTransfer(SlotRef From, ItemTypeId Type, ushort Count, ushort TargetSlot);

/// <summary>One tombstone of a death: the size of its container and the stacks that go into it.</summary>
/// <param name="Size">Slots of the container, 1 to <see cref="Protocol.ProtocolConstants.MaxContainerSlots"/>; every slot is used.</param>
/// <param name="Transfers">The stacks in the order of the plan.</param>
public sealed record TombstonePlan(ushort Size, IReadOnlyList<DeathTransfer> Transfers);

/// <summary>
/// Which stacks a death moves and where (K6-35 D9, D11): the bag, the hotbar and, with the hard penalty, the equipment, each by ascending slots; every non-empty stack
/// to its own empty slot of a tombstone, and a new tombstone for each 256 stacks. A pure function of the state; it changes nothing.
/// </summary>
public sealed class DeathInventoryPlan
{
    private DeathInventoryPlan(IReadOnlyList<TombstonePlan> tombstones, int stackCount)
    {
        Tombstones = tombstones;
        StackCount = stackCount;
    }

    /// <summary>Gets the tombstones; none when the penalty moves nothing or the containers hold nothing (D10).</summary>
    public IReadOnlyList<TombstonePlan> Tombstones { get; }

    /// <summary>Gets the number of stacks that move.</summary>
    public int StackCount { get; }

    /// <summary>Plans the items of a dying player.</summary>
    /// <param name="state">World state.</param>
    /// <param name="player">The player.</param>
    /// <param name="penalty">The penalty of the world.</param>
    /// <returns>The plan.</returns>
    public static DeathInventoryPlan Create(WorldState state, PlayerId player, DeathPenalty penalty)
    {
        ArgumentNullException.ThrowIfNull(state);
        var kinds = DeathRules.MovedKinds(penalty);
        var stacks = new List<(SlotRef From, ItemStack Stack)>();
        for (var k = 0; k < kinds.Count; k++)
        {
            if (!state.Containers.TryGetOfPlayer(player, kinds[k], out var container))
            {
                continue;
            }

            for (var slot = 0; slot < container.Size; slot++)
            {
                var stack = container.GetSlot(slot);
                if (!stack.IsEmpty)
                {
                    stacks.Add((new SlotRef(container.Id, (ushort)slot), stack));
                }
            }
        }

        // 256 stacks to a tombstone: a container holds no more, and the global limit is not raised (D11).
        var tombstones = new List<TombstonePlan>();
        for (var start = 0; start < stacks.Count; start += ProtocolConstants.MaxContainerSlots)
        {
            var size = Math.Min(ProtocolConstants.MaxContainerSlots, stacks.Count - start);
            var transfers = new DeathTransfer[size];
            for (var i = 0; i < size; i++)
            {
                var (from, stack) = stacks[start + i];
                transfers[i] = new DeathTransfer(from, stack.Type, stack.Count, (ushort)i);
            }

            tombstones.Add(new TombstonePlan((ushort)size, transfers));
        }

        return new DeathInventoryPlan(tombstones, stacks.Count);
    }
}
