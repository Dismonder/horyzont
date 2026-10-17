using Horyzont.Core.Items;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Undo journal of one item operation (D5 of K6-29W): the previous content of every slot it wrote, the containers it created and the
/// container counter before it. A refused component rolls back everything in reverse order, so a refused operation leaves no trace.
/// The same records give the changed containers and slots of an accepted operation.
/// </summary>
internal sealed class UndoLog
{
    private readonly List<SlotRecord> _slots = [];
    private readonly List<Container> _created = [];
    private ulong _nextContainerId;

    internal int SlotCount => _slots.Count;

    internal IReadOnlyList<Container> Created => _created;

    internal void Begin(WorldState state)
    {
        _slots.Clear();
        _created.Clear();
        _nextContainerId = state.ContainerIds.NextValue;
    }

    /// <summary>Writes a slot and remembers its previous content.</summary>
    internal void SetSlot(Container container, int slot, ItemStack stack)
    {
        _slots.Add(new SlotRecord(container, (ushort)slot, container.GetSlot(slot)));
        container.SetSlot(slot, stack);
    }

    internal void AddCreated(Container container) => _created.Add(container);

    internal (Container Container, ushort Slot) SlotAt(int index) => (_slots[index].Container, _slots[index].Slot);

    internal void Rollback(WorldState state)
    {
        for (var i = _slots.Count - 1; i >= 0; i--)
        {
            var record = _slots[i];
            record.Container.SetSlot(record.Slot, record.Previous);
        }

        for (var i = _created.Count - 1; i >= 0; i--)
        {
            state.Containers.Remove(_created[i].Id);
        }

        state.ContainerIds.Restore(_nextContainerId);
        _slots.Clear();
        _created.Clear();
    }

    private readonly record struct SlotRecord(Container Container, ushort Slot, ItemStack Previous);
}
