using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Deterministic placement and removal of items in scratch copies of container slots (K6-29W D10, D12). Every planned change is written
/// to the scratch slots, so consecutive calls see each other's results, and appended to the output as an operation.
/// </summary>
internal static class ItemPlacement
{
    /// <summary>
    /// Creates up to <paramref name="count"/> items: first into stacks of the type in the hotbar, then in the bag (slots ascending), then
    /// into empty bag slots, then into empty hotbar slots. One <see cref="ItemCreateOp"/> per slot.
    /// </summary>
    /// <returns>Number of items placed; the rest does not fit.</returns>
    internal static int Create(
        ContainerId hotbarId, Span<ItemStack> hotbar, ContainerId bagId, Span<ItemStack> bag, ItemTypeId type, int count, ushort limit,
        ItemSource source, List<Operation> output)
    {
        var remaining = count;
        remaining = Fill(hotbarId, hotbar, type, remaining, limit, stacks: true, source, output);
        remaining = Fill(bagId, bag, type, remaining, limit, stacks: true, source, output);
        remaining = Fill(bagId, bag, type, remaining, limit, stacks: false, source, output);
        remaining = Fill(hotbarId, hotbar, type, remaining, limit, stacks: false, source, output);
        return count - remaining;
    }

    /// <summary>
    /// Creates up to <paramref name="count"/> items in one container (K6-32 D12): first into stacks of the type, slots ascending, then into empty slots,
    /// slots ascending. One <see cref="ItemCreateOp"/> per slot.
    /// </summary>
    /// <returns>Number of items placed; the rest does not fit.</returns>
    internal static int CreateInContainer(
        ContainerId id, Span<ItemStack> slots, ItemTypeId type, int count, ushort limit, ItemSource source, List<Operation> output)
    {
        var remaining = Fill(id, slots, type, count, limit, stacks: true, source, output);
        remaining = Fill(id, slots, type, remaining, limit, stacks: false, source, output);
        return count - remaining;
    }

    /// <summary>Number of items of a type in the slots.</summary>
    internal static int CountOf(ReadOnlySpan<ItemStack> slots, ItemTypeId type)
    {
        var total = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Type == type)
            {
                total += slots[i].Count;
            }
        }

        return total;
    }

    /// <summary>
    /// Destroys <paramref name="count"/> items of a type, slots ascending; the caller checked with <see cref="CountOf"/> that the slots hold
    /// enough. One <see cref="ItemDestroyOp"/> per slot.
    /// </summary>
    /// <returns>Number of items still to destroy.</returns>
    internal static int Destroy(ContainerId id, Span<ItemStack> slots, ItemTypeId type, int count, ItemSink sink, List<Operation> output)
    {
        var remaining = count;
        for (var i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (slots[i].Type != type)
            {
                continue;
            }

            var taken = Math.Min(remaining, slots[i].Count);
            output.Add(new ItemDestroyOp(new SlotRef(id, (ushort)i), type, (ushort)taken, sink));
            var left = slots[i].Count - taken;
            slots[i] = left == 0 ? ItemStack.Empty : new ItemStack(type, (ushort)left);
            remaining -= taken;
        }

        return remaining;
    }

    /// <summary>
    /// Moves up to <paramref name="count"/> items from <paramref name="from"/> into any slot of a container: stacks of the type first, then
    /// empty slots, ascending. One <see cref="ItemTransferOp"/> per target slot.
    /// </summary>
    /// <returns>Number of items moved.</returns>
    internal static int MoveAny(
        SlotRef from, ContainerId targetId, Span<ItemStack> target, ItemTypeId type, int count, ushort limit, List<Operation> output)
    {
        var remaining = count;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < target.Length && remaining > 0; i++)
            {
                var room = Room(target[i], type, limit, stacks: pass == 0);
                if (room <= 0)
                {
                    continue;
                }

                var moved = Math.Min(room, remaining);
                output.Add(new ItemTransferOp(from, new SlotRef(targetId, (ushort)i), type, (ushort)moved, limit));
                target[i] = new ItemStack(type, (ushort)(target[i].Count + moved));
                remaining -= moved;
            }
        }

        return count - remaining;
    }

    private static int Fill(
        ContainerId id, Span<ItemStack> slots, ItemTypeId type, int count, ushort limit, bool stacks, ItemSource source, List<Operation> output)
    {
        var remaining = count;
        for (var i = 0; i < slots.Length && remaining > 0; i++)
        {
            var room = Room(slots[i], type, limit, stacks);
            if (room <= 0)
            {
                continue;
            }

            var created = Math.Min(room, remaining);
            output.Add(new ItemCreateOp(new SlotRef(id, (ushort)i), type, (ushort)created, limit, source));
            slots[i] = new ItemStack(type, (ushort)(slots[i].Count + created));
            remaining -= created;
        }

        return remaining;
    }

    /// <summary>Free room of a slot for a type: in a stack of the type (<paramref name="stacks"/>) or in an empty slot.</summary>
    private static int Room(ItemStack slot, ItemTypeId type, ushort limit, bool stacks)
    {
        if (stacks)
        {
            return slot.Type == type ? limit - slot.Count : 0;
        }

        return slot.IsEmpty ? limit : 0;
    }
}
