using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Checks preconditions and applies operations. The semantics are part of the journal format: old journals must replay
/// identically, so a change here needs a new operation format version (docs/ZASADY.md, point 3).
/// Reads only the state and the operation, never game content or the terrain (stack limits come with the operation).
/// </summary>
internal sealed class OperationApplier(WorldState state, ITerrainService terrain)
{
    private static readonly IReadOnlyList<TerrainBlockVersion> _noTerrain = [];
    private static readonly IReadOnlyList<ConstructChange> _noConstructs = [];
    private static readonly IReadOnlyList<ContainerChange> _noContainers = [];
    private static readonly ushort[] _noSlots = [];

    // Marks a container in the touched list that changed without a slot (configured or removed).
    private const ushort NoSlot = ushort.MaxValue;
    private readonly List<TerrainBlockKey> _affected = [];
    private readonly UndoLog _undo = new();
    private readonly List<(ulong Container, ushort Slot)> _touched = [];
    private readonly List<BlockFunctionStateSetOp> _pendingStates = [];

    // Chest states removed by the running operation: the chest container must be gone with them by the end of the operation.
    private readonly List<BlockRef> _clearedChestStates = [];
    private readonly List<ulong> _pendingConstructs = [];

    // The block that the last component of a compound writes: the earlier state components see its cell with this block (D12).
    private BlockSetOp? _viewSet;
    private ConstructCreateOp? _viewCreate;

    /// <summary>
    /// Applies an operation when its preconditions hold and sets <see cref="WorldState.LastOpId"/>; otherwise changes nothing. Item and state
    /// operations (31-37, 40, 44, 49) and the components of a <see cref="CompoundOp"/> are applied on the live state with an
    /// <see cref="UndoLog"/>; a refused component rolls everything back. The last component of a compound, a terrain edit or an edit of a
    /// construct, is checked and applied last (D12 of K6-30). Every container changed by the operation gets its version raised by one at
    /// the end, and so does every construct whose function states changed.
    /// </summary>
    /// <param name="id">Operation number.</param>
    /// <param name="tick">Tick.</param>
    /// <param name="actor">Acting player, null for server operations.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="applied">The applied operation on success.</param>
    /// <param name="failure">Why the operation was refused; empty on success.</param>
    /// <param name="componentIndex">Index of the refused component of a compound operation, otherwise -1.</param>
    /// <returns>True when the operation was applied.</returns>
    internal bool TryApply(
        OpId id, Tick tick, PlayerId? actor, Operation operation, [NotNullWhen(true)] out AppliedOperation? applied, out string failure,
        out int componentIndex)
    {
        applied = null;
        componentIndex = -1;
        switch (operation)
        {
            case CompoundOp compound:
                var ok = TryApplyCompound(id, tick, actor, compound, out applied, out failure, out componentIndex);
                EndOperation();
                return ok;
            case var _ when IsComponent(operation):
                BeginOperation();
                failure = ApplyComponent(operation) ?? CheckClearedChestStates() ?? string.Empty;
                if (failure.Length != 0)
                {
                    _undo.Rollback(state);
                    EndOperation();
                    return false;
                }

                applied = Finish(id, tick, actor, operation, _noTerrain, _noConstructs);
                EndOperation();
                return true;
            default:
                if (!CheckPreconditions(operation, out failure))
                {
                    return false;
                }

                applied = Apply(id, tick, actor, operation);
                return true;
        }
    }

    /// <summary>Checks the preconditions of an operation of K1 (1-7) without changing anything; item operations need <see cref="TryApply"/>.</summary>
    internal bool CheckPreconditions(Operation operation, out string failure)
    {
        failure = operation switch
        {
            TerrainEditOp op => CheckTerrainEdit(op),
            ConstructCreateOp op => CheckConstructCreate(op),
            BlockSetOp op => CheckBlockSet(op),
            BlockClearOp op => CheckBlockClear(op) ?? CheckBlockClearOrphans(op),
            PlayerJoinOp op => CheckPlayerJoin(op),
            PlayerLeaveOp op => CheckPlayerLeave(op),
            EntityReparentOp op => CheckReparent(op),
            ShipModeSetOp op => CheckShipModeSet(op),
            MarkerSetOp op => CheckMarkerSet(op),
            MarkerRemoveOp op => CheckMarkerRemove(op),
            ShipFuelAddOp op => CheckShipFuelAdd(op),
            _ => $"Unknown operation {operation.GetType().Name}.",
        } ?? string.Empty;
        return failure.Length == 0;
    }

    /// <summary>Item and state operations: applied with the undo log, alone or as a component of a compound.</summary>
    private static bool IsComponent(Operation operation) =>
        operation is ItemTransferOp or ItemSwapOp or ItemCreateOp or ItemDestroyOp or ContainerCreateOp or ContainerRemoveOp
            or ContainerConfigureOp or BlockEntitySetOp or RespawnPointSetOp or BlockFunctionStateSetOp;

    private void BeginOperation()
    {
        _undo.Begin(state);
        EndOperation();
    }

    /// <summary>Drops the effective view and the pending function states on every exit, so a refused operation leaves nothing for the next one.</summary>
    private void EndOperation()
    {
        _pendingStates.Clear();
        _clearedChestStates.Clear();
        _viewSet = null;
        _viewCreate = null;
    }

    /// <summary>Applies an operation of K1 whose preconditions hold and sets <see cref="WorldState.LastOpId"/>.</summary>
    private AppliedOperation Apply(OpId id, Tick tick, PlayerId? actor, Operation operation)
    {
        var terrainVersions = _noTerrain;
        var constructChanges = _noConstructs;
        switch (operation)
        {
            case TerrainEditOp op:
                terrainVersions = ApplyTerrainEdit(op);
                break;
            case ConstructCreateOp or BlockSetOp or BlockClearOp:
                constructChanges = ApplyConstructEdit(operation);
                break;
            case PlayerJoinOp op:
                ApplyPlayerJoin(op);
                break;
            case PlayerLeaveOp op:
                state.Players.TryGet(op.Player, out var leaving);
                leaving!.Position = op.Position;
                leaving.BodyRotation = op.BodyRotation;
                leaving.Pitch = op.Pitch;
                leaving.IsOnline = false;
                state.Players.MarkChanged(op.Player);
                break;
            case EntityReparentOp op:
                ApplyReparent(op);
                break;
            case ShipModeSetOp op:
                state.Constructs.TryGet(op.Construct, out var switched);
                switched!.SetMode(op.Mode, op.Origin, op.Rotation);
                constructChanges = [new ConstructChange(switched.Id, switched.Version, false)];
                break;
            case MarkerSetOp op:
                state.MarkerIds.MarkUsed(op.Marker);
                state.Markers.Upsert(op.ToRecord());
                break;
            case MarkerRemoveOp op:
                state.Markers.Remove(op.Marker);
                break;

            case ShipFuelAddOp op:
                ApplyShipFuelAdd(op);
                break;
            default:
                throw new InvalidOperationException($"Unknown operation {operation.GetType().Name}.");
        }

        state.SetLastOpId(id);
        return new AppliedOperation(id, tick, actor, operation, terrainVersions, constructChanges, _noContainers);
    }

    /// <summary>Applies a construct edit whose preconditions hold (operations 2, 3, 4).</summary>
    private ConstructChange[] ApplyConstructEdit(Operation operation)
    {
        switch (operation)
        {
            case ConstructCreateOp op:
                state.EntityIds.MarkUsed(op.Construct);
                var created = state.Constructs.Create(op.Construct, op.Parent, op.Origin, op.Rotation);
                created.SetBlock(op.FirstCell, op.Block);
                return [new ConstructChange(created.Id, created.Version, false)];
            case BlockSetOp op:
                state.Constructs.TryGet(op.Construct, out var target);
                target!.SetBlock(op.Cell, op.Block);
                return [new ConstructChange(target.Id, target.Version, false)];
            default:
                var clear = (BlockClearOp)operation;
                state.Constructs.TryGet(clear.Construct, out var cleared);
                cleared!.SetBlock(clear.Cell, BlockState.Empty);
                var removed = cleared.BlockCount == 0;
                if (removed)
                {
                    state.Constructs.Remove(cleared.Id);
                }

                return [new ConstructChange(cleared.Id, cleared.Version, removed)];
        }
    }

    private bool TryApplyCompound(
        OpId id, Tick tick, PlayerId? actor, CompoundOp compound, [NotNullWhen(true)] out AppliedOperation? applied, out string failure,
        out int componentIndex)
    {
        applied = null;
        componentIndex = -1;
        var components = compound.Components;
        if (components is null || components.Count == 0 || components.Count > ProtocolConstants.MaxCompoundComponents)
        {
            failure = $"a compound operation needs 1..{ProtocolConstants.MaxCompoundComponents} components";
            return false;
        }

        var hasFunctionState = false;
        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            var structure = component switch
            {
                null => "missing component",
                CompoundOp => "nested compound operation",
                TerrainEditOp when i != components.Count - 1 => "a terrain edit must be the last component",
                ConstructCreateOp or BlockSetOp or BlockClearOp when i != components.Count - 1 => "a construct edit must be the last component",
                TerrainEditOp or ConstructCreateOp or BlockSetOp or BlockClearOp => null,
                _ when IsComponent(component) => null,
                _ => $"{component.Kind} is not allowed in a compound operation",
            };
            if (structure is not null)
            {
                failure = structure;
                componentIndex = i;
                return false;
            }

            hasFunctionState |= component is BlockFunctionStateSetOp;
        }

        var last = components[^1];
        var lastIsConstructEdit = last is ConstructCreateOp or BlockSetOp or BlockClearOp;
        if (hasFunctionState && lastIsConstructEdit)
        {
            failure = "a function state change cannot share a compound with a construct edit";
            componentIndex = components.Count - 1;
            return false;
        }

        BeginOperation();
        if (lastIsConstructEdit)
        {
            // The conditions of the last component are checked on the state before the compound: no other component changes constructs.
            failure = last switch
            {
                ConstructCreateOp op => CheckConstructCreate(op),
                BlockSetOp op => CheckBlockSet(op),
                _ => CheckBlockClear((BlockClearOp)last),
            } ?? string.Empty;
            if (failure.Length != 0)
            {
                componentIndex = components.Count - 1;
                return false;
            }

            _viewSet = last as BlockSetOp;
            _viewCreate = last as ConstructCreateOp;
        }

        var terrainEdit = last as TerrainEditOp;
        var otherCount = terrainEdit is null && !lastIsConstructEdit ? components.Count : components.Count - 1;
        for (var i = 0; i < otherCount; i++)
        {
            var refused = ApplyComponent(components[i]);
            if (refused is not null)
            {
                _undo.Rollback(state);
                failure = refused;
                componentIndex = i;
                return false;
            }
        }

        // A chest state may be removed before its container inside a compound; judged once all components are applied.
        if (CheckClearedChestStates() is { } inconsistent)
        {
            _undo.Rollback(state);
            failure = inconsistent;
            componentIndex = otherCount - 1;
            return false;
        }

        var terrainVersions = _noTerrain;
        var constructChanges = _noConstructs;
        if (terrainEdit is not null)
        {
            if (!CheckPreconditions(terrainEdit, out failure))
            {
                _undo.Rollback(state);
                componentIndex = components.Count - 1;
                return false;
            }

            terrainVersions = ApplyTerrainEdit(terrainEdit);
        }
        else if (lastIsConstructEdit)
        {
            // No block state and no hosted container may stay on a cell that is cleared (D13), judged after the earlier components.
            if (last is BlockClearOp clear && CheckBlockClearOrphans(clear) is { } orphans)
            {
                _undo.Rollback(state);
                failure = orphans;
                componentIndex = components.Count - 1;
                return false;
            }

            constructChanges = ApplyConstructEdit(last);
        }

        failure = string.Empty;
        applied = Finish(id, tick, actor, compound, terrainVersions, constructChanges);
        return true;
    }

    /// <summary>
    /// Raises the version of every container the undo log touched once, marks it for the checkpoint, applies the function state changes
    /// of the operation (one version bump per construct) and sets LastOpId.
    /// </summary>
    private AppliedOperation Finish(
        OpId id, Tick tick, PlayerId? actor, Operation operation, IReadOnlyList<TerrainBlockVersion> terrainVersions,
        IReadOnlyList<ConstructChange> constructChanges)
    {
        _touched.Clear();
        for (var i = 0; i < _undo.SlotCount; i++)
        {
            var (container, slot) = _undo.SlotAt(i);
            _touched.Add((container.Id.Value, slot));
        }

        var created = _undo.Created;
        for (var i = 0; i < created.Count; i++)
        {
            for (var slot = 0; slot < created[i].Size; slot++)
            {
                _touched.Add((created[i].Id.Value, (ushort)slot));
            }
        }

        // Containers that changed without a slot (operation 37) or were removed (36): listed with a marker instead of a slot.
        for (var i = 0; i < _undo.ConfiguredCount; i++)
        {
            _touched.Add((_undo.ConfiguredAt(i).Id.Value, NoSlot));
        }

        var removedContainers = _undo.Removed;
        for (var i = 0; i < removedContainers.Count; i++)
        {
            _touched.Add((removedContainers[i].Id.Value, NoSlot));
        }

        var changes = _noContainers;
        if (_touched.Count > 0)
        {
            _touched.Sort();

            // The result escapes to observers, so it is built from exact-size arrays: no growing lists, no scratch that outlives the call.
            var groups = 0;
            for (var i = 0; i < _touched.Count; i++)
            {
                if (i == 0 || _touched[i].Container != _touched[i - 1].Container)
                {
                    groups++;
                }
            }

            var list = new ContainerChange[groups];
            var start = 0;
            var group = 0;
            while (start < _touched.Count)
            {
                var containerId = new ContainerId(_touched[start].Container);
                var end = start;
                var distinct = 0;
                while (end < _touched.Count && _touched[end].Container == containerId.Value)
                {
                    if (_touched[end].Slot != NoSlot && (end == start || _touched[end].Slot != _touched[end - 1].Slot))
                    {
                        distinct++;
                    }

                    end++;
                }

                var slots = distinct == 0 ? _noSlots : new ushort[distinct];
                var written = 0;
                for (var k = start; k < end; k++)
                {
                    if (_touched[k].Slot != NoSlot && (k == start || _touched[k].Slot != _touched[k - 1].Slot))
                    {
                        slots[written++] = _touched[k].Slot;
                    }
                }

                if (state.Containers.TryGet(containerId, out var container))
                {
                    container.BumpVersion();
                    state.Containers.MarkChanged(containerId);
                    list[group++] = new ContainerChange(containerId, container.Version, slots);
                }
                else
                {
                    // Removed by the operation: its version is not raised, observers drop it.
                    list[group++] = new ContainerChange(containerId, RemovedVersion(containerId), _noSlots, true);
                }

                start = end;
            }

            changes = list;
        }

        if (_pendingStates.Count > 0)
        {
            constructChanges = ApplyPendingFunctionStates();
        }

        state.SetLastOpId(id);
        return new AppliedOperation(id, tick, actor, operation, terrainVersions, constructChanges, changes);
    }

    private uint RemovedVersion(ContainerId id)
    {
        var removed = _undo.Removed;
        for (var i = 0; i < removed.Count; i++)
        {
            if (removed[i].Id == id)
            {
                return removed[i].Version;
            }
        }

        return 0;
    }

    /// <summary>Writes the collected function states in order, then raises the version of every construct they touched once.</summary>
    private ConstructChange[] ApplyPendingFunctionStates()
    {
        _pendingConstructs.Clear();
        for (var i = 0; i < _pendingStates.Count; i++)
        {
            var op = _pendingStates[i];
            state.Constructs.TryGet(op.Construct, out var construct);
            construct!.ChangeFunctionState(op.Cell, op.State);
            _pendingConstructs.Add(op.Construct.Value);
        }

        _pendingConstructs.Sort();
        var count = 0;
        for (var i = 0; i < _pendingConstructs.Count; i++)
        {
            if (i == 0 || _pendingConstructs[i] != _pendingConstructs[i - 1])
            {
                count++;
            }
        }

        var changes = new ConstructChange[count];
        var written = 0;
        for (var i = 0; i < _pendingConstructs.Count; i++)
        {
            if (i > 0 && _pendingConstructs[i] == _pendingConstructs[i - 1])
            {
                continue;
            }

            state.Constructs.TryGet(new EntityId(_pendingConstructs[i]), out var construct);
            construct!.BumpVersion();
            changes[written++] = new ConstructChange(construct.Id, construct.Version, false);
        }

        return changes;
    }

    /// <summary>Checks and applies one item or state operation through the undo log; returns why it was refused or null.</summary>
    private string? ApplyComponent(Operation operation) => operation switch
    {
        ItemCreateOp op => ApplyItemCreate(op),
        ItemDestroyOp op => ApplyItemDestroy(op),
        ItemTransferOp op => ApplyItemTransfer(op),
        ItemSwapOp op => ApplyItemSwap(op),
        ContainerCreateOp op => ApplyContainerCreate(op),
        ContainerRemoveOp op => ApplyContainerRemove(op),
        ContainerConfigureOp op => ApplyContainerConfigure(op),
        BlockEntitySetOp op => ApplyBlockEntitySet(op),
        RespawnPointSetOp op => ApplyRespawnPointSet(op),
        BlockFunctionStateSetOp op => ApplyFunctionState(op),
        _ => $"{operation.Kind} is not an item or state operation",
    };

    private string? ApplyItemCreate(ItemCreateOp op)
    {
        if (op.Count == 0 || op.StackLimit == 0 || !Enum.IsDefined(op.Source))
        {
            return "count, stack limit or source invalid";
        }

        if (!state.ItemTypes.IsKnown(op.Type))
        {
            return "unknown item type";
        }

        if (!TryGetSlot(op.Slot, out var container, out var current))
        {
            return "unknown container or slot";
        }

        if (!current.IsEmpty && current.Type != op.Type)
        {
            return "slot holds another item type";
        }

        var total = current.Count + op.Count;
        if (total > op.StackLimit)
        {
            return "stack limit exceeded";
        }

        _undo.SetSlot(container, op.Slot.Slot, new ItemStack(op.Type, (ushort)total));
        return null;
    }

    private string? ApplyItemDestroy(ItemDestroyOp op)
    {
        if (op.Count == 0 || !Enum.IsDefined(op.Sink))
        {
            return "count or sink invalid";
        }

        if (!TryGetSlot(op.Slot, out var container, out var current))
        {
            return "unknown container or slot";
        }

        if (current.IsEmpty || current.Type != op.Type || current.Count < op.Count)
        {
            return "slot does not hold the items";
        }

        _undo.SetSlot(container, op.Slot.Slot, Take(current, op.Count));
        return null;
    }

    private string? ApplyItemTransfer(ItemTransferOp op)
    {
        if (op.Count == 0 || op.ToStackLimit == 0)
        {
            return "count or stack limit invalid";
        }

        if (!TryGetSlot(op.From, out var from, out var source) || !TryGetSlot(op.To, out var to, out var target))
        {
            return "unknown container or slot";
        }

        if (op.From == op.To)
        {
            return "source and target are the same slot";
        }

        if (source.IsEmpty || source.Type != op.Type || source.Count < op.Count)
        {
            return "source does not hold the items";
        }

        if (!target.IsEmpty && target.Type != op.Type)
        {
            return "target holds another item type";
        }

        var total = target.Count + op.Count;
        if (total > op.ToStackLimit)
        {
            return "stack limit exceeded";
        }

        _undo.SetSlot(from, op.From.Slot, Take(source, op.Count));
        _undo.SetSlot(to, op.To.Slot, new ItemStack(op.Type, (ushort)total));
        return null;
    }

    private string? ApplyItemSwap(ItemSwapOp op)
    {
        if (!TryGetSlot(op.A, out var a, out var first) || !TryGetSlot(op.B, out var b, out var second))
        {
            return "unknown container or slot";
        }

        if (op.A == op.B)
        {
            return "the slots are the same";
        }

        if (first.Type != op.ExpectedA || second.Type != op.ExpectedB)
        {
            return "slot types differ from the expected ones";
        }

        if (first.IsEmpty && second.IsEmpty)
        {
            return "both slots are empty";
        }

        _undo.SetSlot(a, op.A.Slot, second);
        _undo.SetSlot(b, op.B.Slot, first);
        return null;
    }

    private string? ApplyContainerCreate(ContainerCreateOp op)
    {
        if (!state.ContainerIds.IsUnused(op.Id))
        {
            return "container id already used";
        }

        if (!Enum.IsDefined(op.ContainerKind) || !Enum.IsDefined(op.Access) || op.Size == 0 || op.Size > ProtocolConstants.MaxContainerSlots)
        {
            return "kind, access or size invalid";
        }

        // The codec writes only the player id of a player host and the construct and cell of a block host: any other host field would
        // make the live hash differ from the replay hash. Older journals hold only player hosts; the block host is new (K6-30, D13).
        if (op.Host.Kind == ContainerHostKind.Player)
        {
            if (op.Host != ContainerHost.Player(op.Host.PlayerId))
            {
                return "player host with extra fields";
            }

            var player = op.Host.PlayerId;
            if (!state.Players.TryGet(player, out _))
            {
                return "host player unknown";
            }

            if (state.Containers.TryGetOfPlayer(player, op.ContainerKind, out _))
            {
                return "the player already has a container of this kind";
            }
        }
        else if (op.Host.Kind == ContainerHostKind.ConstructBlock)
        {
            var construct = new EntityId(op.Host.Id);
            if (op.Host != ContainerHost.ConstructBlock(construct, op.Host.Cell))
            {
                return "block host with extra fields";
            }

            if (!op.Host.Cell.IsInBounds || !TryGetViewBlock(construct, op.Host.Cell, out var hostBlock) || hostBlock.IsEmpty)
            {
                return "host block missing";
            }

            if (state.Containers.TryGetOfBlock(construct, op.Host.Cell, op.ContainerKind, out _))
            {
                return "the block already hosts a container of this kind";
            }
        }
        else
        {
            return "host kind not supported";
        }

        if (!op.Owner.IsNone && !state.Players.TryGet(op.Owner, out _))
        {
            return "owner unknown";
        }

        state.ContainerIds.MarkUsed(op.Id);
        _undo.AddCreated(state.Containers.Add(op.Id, op.ContainerKind, op.Host, op.Owner, op.Access, op.Size, 0));
        return null;
    }

    private string? ApplyContainerRemove(ContainerRemoveOp op)
    {
        if (!state.Containers.TryGet(op.Container, out var container))
        {
            return "unknown container";
        }

        if (container.Host.Kind == ContainerHostKind.Player)
        {
            return "the container of a player cannot be removed";
        }

        for (var i = 0; i < container.Size; i++)
        {
            if (!container.GetSlot(i).IsEmpty)
            {
                return "the container is not empty";
            }
        }

        for (var i = 0; i < _undo.Created.Count; i++)
        {
            if (_undo.Created[i] == container)
            {
                return "the container was created by the same operation";
            }
        }

        _undo.Remove(state, container);
        return null;
    }

    private string? ApplyContainerConfigure(ContainerConfigureOp op)
    {
        if (!state.Containers.TryGet(op.Container, out var container))
        {
            return "unknown container";
        }

        if (op.Size == 0 || op.Size > ProtocolConstants.MaxContainerSlots || !Enum.IsDefined(op.Access))
        {
            return "size or access invalid";
        }

        if (op.Size == container.Size && op.Access == container.Access)
        {
            return "size and access are unchanged";
        }

        for (var i = op.Size; i < container.Size; i++)
        {
            if (!container.GetSlot(i).IsEmpty)
            {
                return "slots above the new size are not empty";
            }
        }

        _undo.Configure(container, op.Size, op.Access);
        return null;
    }

    /// <summary>A chest container hosted by a block needs the chest state of that block, as the checkpoint requires; judged at the end of an operation.</summary>
    private string? CheckClearedChestStates()
    {
        for (var i = 0; i < _clearedChestStates.Count; i++)
        {
            var key = _clearedChestStates[i];
            if (!state.BlockEntities.TryGet(key, out _) && state.Containers.IsBlockHost(key.Construct, key.Cell))
            {
                return "a chest container needs the chest state of its block";
            }
        }

        return null;
    }

    private string? ApplyBlockEntitySet(BlockEntitySetOp op)
    {
        var key = new BlockRef(op.Construct, op.Cell);
        state.BlockEntities.TryGet(key, out var existing);
        if (op.State is null)
        {
            // Removing a state judges the live block: the block is still there (D12).
            if (existing is null)
            {
                return "the block has no state";
            }

            if (!state.Constructs.TryGet(op.Construct, out var live) || live.GetBlock(op.Cell).TypeId != op.ExpectedType)
            {
                return "the block differs from the expected type";
            }

            if (existing.Kind == BlockEntityKind.Chest)
            {
                _clearedChestStates.Add(key);
            }

            _undo.SetBlockEntity(state, key, null);
            return null;
        }

        if (!op.Cell.IsInBounds || !TryGetViewBlock(op.Construct, op.Cell, out var block) || block.IsEmpty || block.TypeId != op.ExpectedType)
        {
            return "the cell is empty or holds another block type";
        }

        if (existing is not null && existing.Kind != op.State.Kind)
        {
            return "the kind of a block state cannot change";
        }

        switch (op.State)
        {
            case ChestBlockState chest:
                if (!state.Containers.TryGet(chest.Container, out var container) || container.Kind != ContainerKind.Chest
                    || container.Host != ContainerHost.ConstructBlock(op.Construct, op.Cell))
                {
                    return "the container is not a chest hosted by this block";
                }

                break;
            case BedBlockState bed:
                if (!bed.Owner.IsNone && !state.Players.TryGet(bed.Owner, out _))
                {
                    return "bed owner unknown";
                }

                break;
            default:
                return "block state kind not supported";
        }

        if (existing is null && state.BlockEntities.CountOf(op.Construct) >= ProtocolConstants.MaxBlockEntitiesPerConstruct)
        {
            return "too many block states in the construct";
        }

        _undo.SetBlockEntity(state, key, op.State);
        return null;
    }

    private string? ApplyRespawnPointSet(RespawnPointSetOp op)
    {
        if (!state.Players.TryGet(op.Player, out _))
        {
            return "player unknown";
        }

        if (op.Bed is not { } bed)
        {
            _undo.SetProgress(state, op.Player, null);
            return null;
        }

        if (!bed.Cell.IsInBounds || !TryGetViewBlock(bed.Construct, bed.Cell, out var block) || block.IsEmpty || block.TypeId != bed.ExpectedType)
        {
            return "the cell is empty or holds another block type";
        }

        if (!state.BlockEntities.TryGet(new BlockRef(bed.Construct, bed.Cell), out var entity) || entity is not BedBlockState)
        {
            return "the block is not a bed";
        }

        _undo.SetProgress(state, op.Player, new PlayerProgress(op.Player, bed));
        return null;
    }

    private string? ApplyFunctionState(BlockFunctionStateSetOp op)
    {
        if (op.State >= BlockState.FunctionStateCount)
        {
            return "function state out of range";
        }

        if (!state.Constructs.TryGet(op.Construct, out var construct))
        {
            return "unknown construct";
        }

        if (!op.Cell.IsInBounds || op.ExpectedType == 0 || construct.GetBlock(op.Cell).TypeId != op.ExpectedType)
        {
            return "the cell is empty or holds another block type";
        }

        // Applied with the other function states at the end, when the operation is accepted: no undo needed (D12).
        _pendingStates.Add(op);
        return null;
    }

    /// <summary>The block in a cell as the components of a compound see it (D12): the cell of the last component with its new block.</summary>
    private bool TryGetViewBlock(EntityId construct, CellPos cell, out BlockState block)
    {
        if (_viewCreate is { } create && create.Construct == construct)
        {
            block = cell == create.FirstCell ? create.Block : BlockState.Empty;
            return true;
        }

        if (!state.Constructs.TryGet(construct, out var live))
        {
            block = BlockState.Empty;
            return false;
        }

        block = _viewSet is { } set && set.Construct == construct && set.Cell == cell ? set.Block : live.GetBlock(cell);
        return true;
    }

    private static ItemStack Take(ItemStack stack, ushort count) =>
        stack.Count == count ? ItemStack.Empty : stack with { Count = (ushort)(stack.Count - count) };

    private bool TryGetSlot(SlotRef slot, [NotNullWhen(true)] out Container? container, out ItemStack stack)
    {
        if (state.Containers.TryGet(slot.Container, out container) && slot.Slot < container.Size)
        {
            stack = container.GetSlot(slot.Slot);
            return true;
        }

        container = null;
        stack = default;
        return false;
    }

    private TerrainBlockVersion[] ApplyTerrainEdit(TerrainEditOp op)
    {
        state.Planets.TryGet(op.Planet, out var planet);
        TerrainMath.AffectedBlocks(op.Planet, op.Center, op.Radius, planet!.VoxelSizeMeters, TerrainRules.EditMarginVoxels, _affected);
        var versions = new TerrainBlockVersion[_affected.Count];
        for (var i = 0; i < _affected.Count; i++)
        {
            versions[i] = new TerrainBlockVersion(_affected[i], state.TerrainVersions.Bump(_affected[i]));
        }

        terrain.ApplyEdit(op, versions);
        return versions;
    }

    private void ApplyPlayerJoin(PlayerJoinOp op)
    {
        if (state.Players.TryGet(op.Player, out var known))
        {
            known.DisplayName = op.DisplayName;
            known.Position = op.Position;
            known.BodyRotation = op.BodyRotation;
            known.IsOnline = true;
            state.Players.MarkChanged(op.Player);
            return;
        }

        state.PlayerIds.MarkUsed(op.Player);
        state.EntityIds.MarkUsed(op.Entity);
        state.Players.Add(new PlayerState(
            op.Player, op.Identity, op.DisplayName, op.Entity, op.Frame, op.ParentSeq, null, op.Position, op.BodyRotation, 0f, true));
    }

    private void ApplyReparent(EntityReparentOp op)
    {
        if (state.Players.TryGetByEntity(op.Entity, out var player))
        {
            player.Frame = op.NewParent;
            player.ParentSeq = op.NewParentSeq;
            player.Position = op.LocalPosition;
            player.BodyRotation = op.LocalRotation;
            player.Seat = op.SeatCell;
            state.Players.MarkChanged(player.Player);
            return;
        }

        state.Constructs.TryGet(op.Entity, out var construct);
        construct!.Reparent(op.NewParent, op.NewParentSeq, op.LocalPosition, op.LocalRotation);
    }

    private string? CheckTerrainEdit(TerrainEditOp op)
    {
        if (!state.Planets.TryGet(op.Planet, out var planet))
        {
            return "unknown planet";
        }

        if (!TerrainRules.CheckEdit(op, planet, out var failure))
        {
            return failure;
        }

        if (op.Mode == TerrainEditMode.Remove ? op.MaterialId != 0 : !state.Materials.IsKnown(op.MaterialId))
        {
            return "invalid material";
        }

        return null;
    }

    private string? CheckConstructCreate(ConstructCreateOp op)
    {
        if (!state.EntityIds.IsUnused(op.Construct))
        {
            return "construct id already used";
        }

        if (op.Parent.Kind != FrameKind.Planet || !state.Planets.TryGet(new PlanetId(op.Parent.Id), out _))
        {
            return "parent is not a known planet";
        }

        if (!op.FirstCell.IsInBounds)
        {
            return "cell out of bounds";
        }

        if (!Geometry.IsFinite(op.Origin) || !Geometry.IsRotation(op.Rotation))
        {
            return "invalid pose";
        }

        return CheckBlock(op.Block);
    }

    private string? CheckBlockSet(BlockSetOp op)
    {
        if (!state.Constructs.TryGet(op.Construct, out var construct))
        {
            return "unknown construct";
        }

        if (!op.Cell.IsInBounds || !construct.GetBlock(op.Cell).IsEmpty)
        {
            return "cell out of bounds or occupied";
        }

        return CheckBlock(op.Block);
    }

    private string? CheckBlockClear(BlockClearOp op)
    {
        if (!state.Constructs.TryGet(op.Construct, out var construct))
        {
            return "unknown construct";
        }

        if (!op.Cell.IsInBounds || construct.GetBlock(op.Cell).IsEmpty)
        {
            return "cell out of bounds or empty";
        }

        if (state.TryGetSeated(op.Construct, op.Cell, out _))
        {
            return "a player sits in the cell";
        }

        if (construct.BlockCount == 1 && state.HasChildren(FrameRef.Of(op.Construct)))
        {
            return "the last block of a frame with children";
        }

        return null;
    }

    /// <summary>D13 of K6-30: a cell with a block state or a hosted container is not cleared, or the items would be orphaned.</summary>
    private string? CheckBlockClearOrphans(BlockClearOp op)
    {
        if (state.BlockEntities.TryGet(new BlockRef(op.Construct, op.Cell), out _))
        {
            return "the cell has a block state";
        }

        return state.Containers.IsBlockHost(op.Construct, op.Cell) ? "the cell hosts a container" : null;
    }

    private string? CheckPlayerJoin(PlayerJoinOp op)
    {
        if (!IsDisplayName(op.DisplayName) || !Geometry.IsFinite(op.Position) || !Geometry.IsRotation(op.BodyRotation))
        {
            return "invalid name or pose";
        }

        if (state.Players.TryGet(op.Player, out var known))
        {
            if (known.IsOnline)
            {
                return "player already online";
            }

            if (known.Identity != op.Identity || known.Entity != op.Entity || known.Frame != op.Frame || known.ParentSeq != op.ParentSeq)
            {
                return "identity, entity, frame or ParentSeq differ from the known player";
            }

            return null;
        }

        if (!state.PlayerIds.IsUnused(op.Player))
        {
            return "player id already used";
        }

        if (state.Players.TryGetByIdentity(op.Identity, out _))
        {
            return "identity belongs to another player";
        }

        if (!state.EntityIds.IsUnused(op.Entity))
        {
            return "entity id already used";
        }

        if (!state.FrameExists(op.Frame) || op.ParentSeq != 0)
        {
            return "frame missing or ParentSeq not 0";
        }

        return null;
    }

    private string? CheckPlayerLeave(PlayerLeaveOp op)
    {
        if (!state.Players.TryGet(op.Player, out var player) || !player.IsOnline)
        {
            return "player unknown or offline";
        }

        if (player.Frame != op.Frame || player.ParentSeq != op.ParentSeq)
        {
            return "frame or ParentSeq differ";
        }

        if (!Geometry.IsFinite(op.Position) || !Geometry.IsRotation(op.BodyRotation) || !float.IsFinite(op.Pitch))
        {
            return "invalid pose";
        }

        return null;
    }

    private string? CheckReparent(EntityReparentOp op)
    {
        uint currentSeq;
        var isPlayer = state.Players.TryGetByEntity(op.Entity, out var player);
        if (isPlayer)
        {
            currentSeq = player!.ParentSeq;
        }
        else if (state.Constructs.TryGet(op.Entity, out var construct))
        {
            currentSeq = construct.ParentSeq;
        }
        else
        {
            return "entity has no frame";
        }

        if (currentSeq == uint.MaxValue || op.NewParentSeq != currentSeq + 1)
        {
            return "NewParentSeq is not current + 1";
        }

        if (!state.FrameExists(op.NewParent))
        {
            return "new frame does not exist";
        }

        if (!isPlayer && IsSelfOrDescendant(op.NewParent, op.Entity))
        {
            return "new frame is the construct or its descendant";
        }

        if (!Geometry.IsFinite(op.LocalPosition) || !Geometry.IsFinite(op.LocalVelocity) || !Geometry.IsRotation(op.LocalRotation))
        {
            return "invalid pose";
        }

        if (op.SeatCell is { } seat)
        {
            if (!isPlayer || op.NewParent.Kind != FrameKind.Construct)
            {
                return "seat only for a player entering a construct";
            }

            var seatConstruct = new EntityId(op.NewParent.Id);
            state.Constructs.TryGet(seatConstruct, out var target);
            if (!seat.IsInBounds || target!.GetBlock(seat).IsEmpty)
            {
                return "seat cell empty";
            }

            if (state.TryGetSeated(seatConstruct, seat, out var seated) && seated.Player != player!.Player)
            {
                return "seat taken";
            }
        }

        return null;
    }

    /// <summary>K5-24 decision 13: only hashed state (the construct is a ship) and the values; never the ship state or the content.</summary>
    private string? CheckShipFuelAdd(ShipFuelAddOp op)
    {
        if (!state.Constructs.TryGet(op.Construct, out var construct))
        {
            return "unknown construct";
        }

        if (construct.Mode != ConstructMode.Ship)
        {
            return "not a ship";
        }

        return float.IsFinite(op.Units) && op.Units > 0f && float.IsFinite(op.Capacity) && op.Capacity >= 0f ? null : "invalid fuel units or capacity";
    }

    private void ApplyShipFuelAdd(ShipFuelAddOp op)
    {
        var record = state.ShipStates.TryGet(op.Construct, out var current)
            ? current
            : new ShipStateRecord(op.Construct, 0f, Vector3.Zero, Vector3.Zero, ShipStateFlags.Parked | ShipStateFlags.GearDown);
        state.ShipStates.Set(record with { Fuel = MathF.Min(record.Fuel + op.Units, op.Capacity) });
        state.Tracker.MarkShipState(op.Construct);
    }

    private string? CheckShipModeSet(ShipModeSetOp op)
    {
        if (!state.Constructs.TryGet(op.Construct, out var construct))
        {
            return "unknown construct";
        }

        if (op.Mode is not (ConstructMode.Static or ConstructMode.Ship) || op.Mode == construct.Mode)
        {
            return "mode unknown or unchanged";
        }

        if (op.Parent != construct.Parent || op.ParentSeq != construct.ParentSeq)
        {
            return "parent or ParentSeq differ";
        }

        if (op.Mode == ConstructMode.Static && op.Parent.Kind != FrameKind.Planet)
        {
            return "a static construct exists only in a planet frame";
        }

        return IsPose(op.Origin, op.Rotation) ? null : "invalid pose";
    }

    private string? CheckMarkerSet(MarkerSetOp op)
    {
        if (op.Marker.IsNone || !state.Planets.TryGet(op.Planet, out _))
        {
            return "marker id none or unknown planet";
        }

        if (!MarkerRules.IsValidPosition(op.Position) || !MarkerRules.IsValidName(op.Name) || !MarkerRules.IsValidStyle(op.Icon, op.Color))
        {
            return "invalid marker position, name or style";
        }

        if (state.Markers.TryGet(op.Marker, out var existing))
        {
            return existing.Owner == op.Owner ? null : "the owner of a marker cannot change";
        }

        if (!state.MarkerIds.IsUnused(op.Marker))
        {
            return "marker id below the next id and unknown";
        }

        var count = op.Owner is { } owner ? state.Markers.CountPrivate(owner) : state.Markers.CountShared;
        var limit = op.Owner is null ? MarkerRules.MaxShared : MarkerRules.MaxPrivatePerPlayer;
        return count >= limit ? "marker limit reached" : null;
    }

    private string? CheckMarkerRemove(MarkerRemoveOp op) => state.Markers.TryGet(op.Marker, out _) ? null : "unknown marker";

    private bool IsSelfOrDescendant(FrameRef frame, EntityId construct)
    {
        for (var depth = 0; depth <= state.Constructs.Count; depth++)
        {
            if (frame.Kind != FrameKind.Construct)
            {
                return false;
            }

            if (frame.Id == construct.Value)
            {
                return true;
            }

            if (!state.Constructs.TryGet(new EntityId(frame.Id), out var parent))
            {
                return false;
            }

            frame = parent.Parent;
        }

        return true;
    }

    private string? CheckBlock(BlockState block)
    {
        if (block.IsEmpty || !block.IsWellFormed || !state.BlockTypes.IsKnown(block.TypeId))
        {
            return "block malformed or of unknown type";
        }

        return null;
    }

    internal static bool IsDisplayName(string? name) =>
        !string.IsNullOrEmpty(name) && Encoding.UTF8.GetByteCount(name) <= ProtocolConstants.MaxDisplayNameBytes;

    internal static bool IsPose(Vector3 position, Quaternion rotation) => Geometry.IsFinite(position) && Geometry.IsRotation(rotation);
}
