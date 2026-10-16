using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Progress;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Undo journal of one operation made of item and state components (D5 of K6-29W, extended by D12 of K6-30): the previous content of
/// every slot, container size and access, block state and respawn point it wrote, the containers it created or removed and the
/// container counter before it. A refused component rolls back everything in reverse order of the changes, so a refused operation
/// leaves no trace. The same records give the changed containers and slots of an accepted operation.
/// </summary>
internal sealed class UndoLog
{
    private readonly List<SlotRecord> _slots = [];
    private readonly List<Container> _created = [];
    private readonly List<Container> _removed = [];
    private readonly List<ConfigureRecord> _configured = [];
    private readonly List<BlockEntityRecordUndo> _entities = [];
    private readonly List<ProgressRecordUndo> _progress = [];
    private readonly List<PropRecordUndo> _props = [];
    private readonly List<Step> _steps = [];
    private ulong _nextContainerId;

    internal int SlotCount => _slots.Count;

    internal IReadOnlyList<Container> Created => _created;

    internal IReadOnlyList<Container> Removed => _removed;

    internal int ConfiguredCount => _configured.Count;

    internal void Begin(WorldState state)
    {
        _slots.Clear();
        _created.Clear();
        _removed.Clear();
        _configured.Clear();
        _entities.Clear();
        _progress.Clear();
        _props.Clear();
        _steps.Clear();
        _nextContainerId = state.ContainerIds.NextValue;
    }

    /// <summary>Writes a slot and remembers its previous content.</summary>
    internal void SetSlot(Container container, int slot, ItemStack stack)
    {
        _steps.Add(new Step(StepKind.Slot, _slots.Count));
        _slots.Add(new SlotRecord(container, (ushort)slot, container.GetSlot(slot)));
        container.SetSlot(slot, stack);
    }

    internal void AddCreated(Container container)
    {
        _steps.Add(new Step(StepKind.Created, _created.Count));
        _created.Add(container);
    }

    /// <summary>Resizes a container and sets its access, remembering the previous values.</summary>
    internal void Configure(Container container, ushort size, ContainerAccess access)
    {
        _steps.Add(new Step(StepKind.Configure, _configured.Count));
        _configured.Add(new ConfigureRecord(container, (ushort)container.Size, container.Access));
        container.Configure(size, access);
    }

    /// <summary>Removes a container from the store, remembering it.</summary>
    internal void Remove(WorldState state, Container container)
    {
        _steps.Add(new Step(StepKind.Remove, _removed.Count));
        _removed.Add(container);
        state.Containers.Remove(container.Id);
        state.Containers.MarkChanged(container.Id);
    }

    /// <summary>Stores a block state (null removes it), remembering the previous one.</summary>
    internal void SetBlockEntity(WorldState state, BlockRef key, BlockEntityState? value)
    {
        var previous = value is null ? state.BlockEntities.Remove(key) : state.BlockEntities.Set(key, value);
        _steps.Add(new Step(StepKind.BlockEntity, _entities.Count));
        _entities.Add(new BlockEntityRecordUndo(key, previous));
    }

    /// <summary>Stores the progress of a player (null removes it), remembering the previous one.</summary>
    internal void SetProgress(WorldState state, PlayerId player, PlayerProgress? value)
    {
        var previous = value is null ? state.Progress.Remove(player) : state.Progress.Set(value);
        _steps.Add(new Step(StepKind.Progress, _progress.Count));
        _progress.Add(new ProgressRecordUndo(player, previous));
    }

    /// <summary>Adds a prop to the harvested set, remembering that it was not there.</summary>
    internal void SetHarvested(WorldState state, PlanetId planet, PropId prop, Tick tick)
    {
        state.HarvestedProps.Add(planet, prop, tick);
        _steps.Add(new Step(StepKind.Prop, _props.Count));
        _props.Add(new PropRecordUndo(planet, prop, null));
    }

    /// <summary>Removes a prop from the harvested set, remembering its tick; the caller checked that it is in the set.</summary>
    internal void SetRegrown(WorldState state, PlanetId planet, PropId prop)
    {
        state.HarvestedProps.Remove(planet, prop, out var previous);
        _steps.Add(new Step(StepKind.Prop, _props.Count));
        _props.Add(new PropRecordUndo(planet, prop, previous));
    }

    internal (Container Container, ushort Slot) SlotAt(int index) => (_slots[index].Container, _slots[index].Slot);

    internal Container ConfiguredAt(int index) => _configured[index].Container;

    internal void Rollback(WorldState state)
    {
        for (var i = _steps.Count - 1; i >= 0; i--)
        {
            var step = _steps[i];
            switch (step.Kind)
            {
                case StepKind.Slot:
                    var slot = _slots[step.Index];
                    slot.Container.SetSlot(slot.Slot, slot.Previous);
                    break;
                case StepKind.Created:
                    state.Containers.Remove(_created[step.Index].Id);
                    break;
                case StepKind.Configure:
                    var configured = _configured[step.Index];
                    configured.Container.Configure(configured.Size, configured.Access);
                    break;
                case StepKind.Remove:
                    state.Containers.Restore(_removed[step.Index]);
                    break;
                case StepKind.BlockEntity:
                    var entity = _entities[step.Index];
                    if (entity.Previous is null)
                    {
                        state.BlockEntities.Remove(entity.Key);
                    }
                    else
                    {
                        state.BlockEntities.Set(entity.Key, entity.Previous);
                    }

                    break;
                case StepKind.Prop:
                    var prop = _props[step.Index];
                    if (prop.PreviousTick is { } previousTick)
                    {
                        state.HarvestedProps.Add(prop.Planet, prop.Prop, previousTick);
                    }
                    else
                    {
                        state.HarvestedProps.Remove(prop.Planet, prop.Prop, out _);
                    }

                    break;
                default:
                    var progress = _progress[step.Index];
                    if (progress.Previous is null)
                    {
                        state.Progress.Remove(progress.Player);
                    }
                    else
                    {
                        state.Progress.Set(progress.Previous);
                    }

                    break;
            }
        }

        state.ContainerIds.Restore(_nextContainerId);
        _slots.Clear();
        _created.Clear();
        _removed.Clear();
        _configured.Clear();
        _entities.Clear();
        _progress.Clear();
        _props.Clear();
        _steps.Clear();
    }

    private enum StepKind : byte
    {
        Slot,
        Created,
        Configure,
        Remove,
        BlockEntity,
        Progress,
        Prop,
    }

    private readonly record struct Step(StepKind Kind, int Index);

    private readonly record struct SlotRecord(Container Container, ushort Slot, ItemStack Previous);

    private readonly record struct ConfigureRecord(Container Container, ushort Size, ContainerAccess Access);

    private readonly record struct BlockEntityRecordUndo(BlockRef Key, BlockEntityState? Previous);

    private readonly record struct ProgressRecordUndo(PlayerId Player, PlayerProgress? Previous);

    private readonly record struct PropRecordUndo(PlanetId Planet, PropId Prop, Tick? PreviousTick);
}
