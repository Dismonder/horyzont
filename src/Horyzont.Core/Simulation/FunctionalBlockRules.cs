using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Resolution of the functional blocks of K6-30: putting up and taking down chests, beds and lamps, door and lamp switches, sleeping,
/// the access setting of a chest, the quick stack and take all. Reads the block content and, for items, the survival content, in the
/// resolver only (ZASADY §4): every value the operations depend on (the size of a chest, the stack limits, the state of a lamp) is carried
/// by the operations. The intent validator has checked the intent before; the order of the checks is in docs/plans/K6-30-bloki-funkcyjne.md,
/// "Walidacja i rozwiązanie intencji".
/// </summary>
internal sealed class FunctionalBlockRules(WorldState state, GameRules rules, BlockContentTable content, SurvivalContent? survival)
{
    /// <summary>The state of a lamp that was just put up (K6-30 D7).</summary>
    private const byte LampOn = 1;

    private readonly List<Operation> _components = [];

    /// <summary>
    /// True when the last <see cref="TryResolve"/> refused only after it began to scan the chest index of the world (the costly part of a quick stack): the
    /// caller then charges the rate limit of item intents for the refusal, so a client cannot repeat it faster than accepted intents.
    /// </summary>
    internal bool RefusalCostsToken { get; private set; }
    private readonly List<CellPos> _column = [];

    /// <summary>
    /// Resolves the intents this class owns.
    /// </summary>
    /// <param name="player">Acting player.</param>
    /// <param name="intent">The validated intent.</param>
    /// <param name="facts">Facts of the validator.</param>
    /// <param name="operation">The operation when <paramref name="reason"/> is <see cref="RejectReason.None"/>.</param>
    /// <param name="reason">The refusal, or none.</param>
    /// <returns>False when the intent is not a functional block intent (the caller resolves it otherwise).</returns>
    internal bool TryResolve(PlayerId player, Intent intent, IntentFacts facts, out Operation operation, out RejectReason reason)
    {
        operation = null!;
        reason = RejectReason.None;
        RefusalCostsToken = false;
        switch (intent)
        {
            case PlaceBlockIntent place:
                var kind = content.Get(place.BlockTypeId).Function.Kind;
                if (kind is not (BlockFunctionKind.Chest or BlockFunctionKind.Bed or BlockFunctionKind.Light))
                {
                    return false;
                }

                reason = ResolvePlace(player, place, facts, kind, out operation);
                return true;

            case RemoveBlockIntent remove:
                if (!HasFunctionalState(remove.Construct, remove.Cell))
                {
                    return false;
                }

                reason = ResolveRemove(player, remove, out operation);
                return true;

            case BlockInteractIntent interact:
                reason = ResolveInteract(player, interact, out operation);
                return true;

            case BlockConfigIntent config:
                reason = ResolveConfig(config, out operation);
                return true;

            case QuickStackIntent:
                reason = ResolveQuickStack(player, out operation);
                return true;

            case TakeAllIntent takeAll:
                reason = ResolveTakeAll(player, takeAll, out operation);
                return true;

            default:
                return false;
        }
    }

    /// <summary>True when the cell has a block state or hosts a container, so it cannot be cleared by a plain <see cref="BlockClearOp"/>.</summary>
    internal bool HasFunctionalState(EntityId construct, CellPos cell) =>
        state.BlockEntities.TryGet(new BlockRef(construct, cell), out _) || state.Containers.IsBlockHost(construct, cell);

    /// <summary>
    /// The cells a door switch toggles together (K6-30 D6): the aimed cell, then cells of the same block type below it, then above it, in
    /// the Y direction of the construct, at most <paramref name="maxCells"/> in all. Ascending by Y.
    /// </summary>
    /// <param name="construct">The construct.</param>
    /// <param name="aimed">The aimed cell.</param>
    /// <param name="maxCells">Most cells in the column.</param>
    /// <param name="output">Receives the cells (cleared first).</param>
    internal static void DoorColumn(Construct construct, CellPos aimed, int maxCells, List<CellPos> output)
    {
        output.Clear();
        var type = construct.GetBlock(aimed).TypeId;
        var down = 0;
        while (down + 1 < maxCells && IsSameType(construct, aimed with { Y = aimed.Y - down - 1 }, type))
        {
            down++;
        }

        var up = 0;
        while (down + up + 1 < maxCells && IsSameType(construct, aimed with { Y = aimed.Y + up + 1 }, type))
        {
            up++;
        }

        for (var y = aimed.Y - down; y <= aimed.Y + up; y++)
        {
            output.Add(aimed with { Y = y });
        }
    }

    private static bool IsSameType(Construct construct, CellPos cell, ushort type) => cell.IsInBounds && construct.GetBlock(cell).TypeId == type;

    private RejectReason ResolvePlace(PlayerId player, PlaceBlockIntent place, IntentFacts facts, BlockFunctionKind kind, out Operation operation)
    {
        var block = IntentResolver.Resolve(state, player, place, facts);
        EntityId construct;
        CellPos cell;
        switch (block)
        {
            case BlockSetOp set:
                construct = set.Construct;
                cell = set.Cell;
                break;
            case ConstructCreateOp create:
                construct = create.Construct;
                cell = create.FirstCell;
                break;
            default:
                throw new InvalidOperationException($"A block placement resolved to {block.Kind}.");
        }

        if (kind == BlockFunctionKind.Light)
        {
            operation = block switch
            {
                BlockSetOp set => set with { Block = set.Block.WithFunctionState(LampOn) },
                _ => ((ConstructCreateOp)block) with { Block = ((ConstructCreateOp)block).Block.WithFunctionState(LampOn) },
            };
            return RejectReason.None;
        }

        operation = null!;
        if (survival is null)
        {
            return RejectReason.InvalidParameters;
        }

        if (kind == BlockFunctionKind.Bed)
        {
            operation = new CompoundOp([new BlockEntitySetOp(construct, cell, place.BlockTypeId, new BedBlockState(PlayerId.None)), block]);
            return RejectReason.None;
        }

        var slots = content.Get(place.BlockTypeId).Function.ContainerSlots;
        if (slots is 0 or > ProtocolConstants.MaxContainerSlots)
        {
            return RejectReason.InvalidParameters;
        }

        var container = state.ContainerIds.PeekNext();
        operation = new CompoundOp(
        [
            new ContainerCreateOp(container, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, cell), player, ContainerAccess.Shared, slots),
            new BlockEntitySetOp(construct, cell, place.BlockTypeId, new ChestBlockState(container)),
            block,
        ]);
        return RejectReason.None;
    }

    private RejectReason ResolveRemove(PlayerId player, RemoveBlockIntent remove, out Operation operation)
    {
        operation = null!;
        if (!state.Constructs.TryGet(remove.Construct, out var construct))
        {
            return RejectReason.UnknownConstruct;
        }

        var type = construct.GetBlock(remove.Cell).TypeId;
        _components.Clear();
        if (state.BlockEntities.TryGet(new BlockRef(remove.Construct, remove.Cell), out var entity))
        {
            switch (entity)
            {
                case ChestBlockState chest:
                    if (!state.Containers.TryGet(chest.Container, out var container))
                    {
                        return RejectReason.InvalidParameters;
                    }

                    // A private chest is taken down by its owner only; a chest that still holds items is not taken down at all (D8).
                    if (container.Access == ContainerAccess.Private && container.Owner != player)
                    {
                        return RejectReason.NotOwner;
                    }

                    for (var slot = 0; slot < container.Size; slot++)
                    {
                        if (!container.GetSlot(slot).IsEmpty)
                        {
                            return RejectReason.ContainerNotEmpty;
                        }
                    }

                    _components.Add(new ContainerRemoveOp(container.Id));
                    break;

                case BedBlockState { Owner.IsNone: false } bed:
                    if (state.Progress.TryGet(bed.Owner, out var progress)
                        && progress.RespawnBed is { } point && point.Construct == remove.Construct && point.Cell == remove.Cell)
                    {
                        _components.Add(new RespawnPointSetOp(bed.Owner, null));
                    }

                    break;

                case BedBlockState:
                    break;

                default:
                    return RejectReason.InvalidParameters;
            }

            _components.Add(new BlockEntitySetOp(remove.Construct, remove.Cell, type, null));
        }
        else if (state.Containers.IsBlockHost(remove.Construct, remove.Cell))
        {
            // A container without a block state is not something a player can take down (the state is the only way to find it).
            return RejectReason.InvalidParameters;
        }

        _components.Add(new BlockClearOp(remove.Construct, remove.Cell));
        operation = new CompoundOp(_components.ToArray());
        return RejectReason.None;
    }

    private RejectReason ResolveInteract(PlayerId player, BlockInteractIntent intent, out Operation operation)
    {
        operation = null!;
        if (!state.Constructs.TryGet(intent.Construct, out var construct))
        {
            return RejectReason.UnknownConstruct;
        }

        var block = construct.GetBlock(intent.Cell);
        var kind = content.Get(block.TypeId).Function.Kind;
        if (intent.Action == BlockInteractAction.Toggle)
        {
            var next = (byte)(block.FunctionState == 0 ? 1 : 0);
            switch (kind)
            {
                case BlockFunctionKind.Light:
                    operation = new BlockFunctionStateSetOp(intent.Construct, intent.Cell, block.TypeId, next);
                    return RejectReason.None;

                case BlockFunctionKind.Door:
                    DoorColumn(construct, intent.Cell, rules.MaxDoorColumnCells, _column);
                    if (_column.Count == 1)
                    {
                        operation = new BlockFunctionStateSetOp(intent.Construct, intent.Cell, block.TypeId, next);
                        return RejectReason.None;
                    }

                    var switches = new Operation[_column.Count];
                    for (var i = 0; i < switches.Length; i++)
                    {
                        switches[i] = new BlockFunctionStateSetOp(intent.Construct, _column[i], block.TypeId, next);
                    }

                    operation = new CompoundOp(switches);
                    return RejectReason.None;

                default:
                    return RejectReason.InvalidParameters;
            }
        }

        return kind == BlockFunctionKind.Bed ? ResolveSleep(player, intent, block.TypeId, out operation) : RejectReason.InvalidParameters;
    }

    /// <summary>
    /// Sleeping and setting the respawn point have the same effect in this package (rest is K6-33): the bed becomes the one bed of the
    /// player. The old bed of the player is released, a free bed is claimed, and the respawn point moves to the bed (D10).
    /// </summary>
    private RejectReason ResolveSleep(PlayerId player, BlockInteractIntent intent, ushort type, out Operation operation)
    {
        operation = null!;
        if (!state.BlockEntities.TryGet(new BlockRef(intent.Construct, intent.Cell), out var entity) || entity is not BedBlockState bed)
        {
            return RejectReason.InvalidParameters;
        }

        if (!bed.Owner.IsNone && bed.Owner != player)
        {
            return RejectReason.NotOwner;
        }

        _components.Clear();
        if (state.Progress.TryGet(player, out var progress) && progress.RespawnBed is { } old && (old.Construct != intent.Construct || old.Cell != intent.Cell)
            && state.BlockEntities.TryGet(new BlockRef(old.Construct, old.Cell), out var oldEntity) && oldEntity is BedBlockState oldBed && oldBed.Owner == player
            && state.Constructs.TryGet(old.Construct, out var oldConstruct))
        {
            _components.Add(new BlockEntitySetOp(old.Construct, old.Cell, oldConstruct.GetBlock(old.Cell).TypeId, new BedBlockState(PlayerId.None)));
        }

        if (bed.Owner.IsNone)
        {
            _components.Add(new BlockEntitySetOp(intent.Construct, intent.Cell, type, new BedBlockState(player)));
        }

        _components.Add(new RespawnPointSetOp(player, new BedRef(intent.Construct, intent.Cell, type)));
        operation = _components.Count == 1 ? _components[0] : new CompoundOp(_components.ToArray());
        return RejectReason.None;
    }

    private RejectReason ResolveConfig(BlockConfigIntent intent, out Operation operation)
    {
        operation = null!;
        if (intent.Config is not ContainerAccessConfig access
            || !state.BlockEntities.TryGet(new BlockRef(intent.Construct, intent.Cell), out var entity) || entity is not ChestBlockState chest
            || !state.Containers.TryGet(chest.Container, out var container))
        {
            return RejectReason.InvalidParameters;
        }

        operation = new ContainerConfigureOp(container.Id, (ushort)container.Size, access.Access);
        return RejectReason.None;
    }

    /// <summary>
    /// Quick stack (D16): items of the own bag go to the chests in reach that already hold the type, nearest chest first (ties: lower
    /// container id): first the stacks of the type of all of them, then their empty slots.
    /// </summary>
    private RejectReason ResolveQuickStack(PlayerId player, out Operation operation)
    {
        operation = null!;
        if (survival is null || !state.Containers.TryGetOfPlayer(player, ContainerKind.PlayerBag, out var bag))
        {
            return RejectReason.InvalidParameters;
        }

        // Nothing to put away: refuse before looking at any chest (no scan, no allocation on this path).
        var hasItems = false;
        for (var slot = 0; slot < bag.Size && !hasItems; slot++)
        {
            hasItems = !bag.GetSlot(slot).IsEmpty;
        }

        if (!hasItems)
        {
            return RejectReason.NotEnoughItems;
        }

        // Only the chest index is visited, never every container of the world; the list is built once a chest is known to be in reach.
        // The eye is measured once: a chest out of reach costs one distance, not a lookup of the player.
        if (!state.Players.TryGet(player, out var playerState) || !ContainerReach.TryGetEye(state, rules, playerState, out var eyePlanet, out var eye))
        {
            return RejectReason.NotEnoughItems;
        }

        // From here on the chest index of the world is scanned (linear in the chests of the world): whatever is refused has cost something.
        RefusalCostsToken = true;
        var chestIndex = state.Containers.OfKind(ContainerKind.Chest);
        List<ChestCopy>? chests = null;
        for (var i = 0; i < chestIndex.Count; i++)
        {
            var container = chestIndex[i];
            if (container.Host.Kind != ContainerHostKind.ConstructBlock
                || (container.Access != ContainerAccess.Shared && container.Owner != player)
                || !ContainerReach.TryDistanceFrom(state, eyePlanet, eye, new EntityId(container.Host.Id), container.Host.Cell, out var meters)
                || meters > rules.QuickStackRangeMeters)
            {
                continue;
            }

            (chests ??= []).Add(new ChestCopy(container.Id, container.Slots.ToArray(), meters));
        }

        if (chests is null)
        {
            return RejectReason.NotEnoughItems;
        }

        chests.Sort(static (a, b) =>
        {
            var byDistance = a.Meters.CompareTo(b.Meters);
            return byDistance != 0 ? byDistance : a.Id.Value.CompareTo(b.Id.Value);
        });

        _components.Clear();
        var anyPending = false;
        for (var slot = 0; slot < bag.Size && _components.Count < ProtocolConstants.MaxCompoundComponents; slot++)
        {
            var stack = bag.GetSlot(slot);
            if (stack.IsEmpty || !TryGetDef(stack.Type, out var def))
            {
                continue;
            }

            var eligible = false;
            for (var c = 0; c < chests.Count; c++)
            {
                eligible |= chests[c].Holds(stack.Type);
            }

            if (!eligible)
            {
                continue;
            }

            anyPending = true;
            var remaining = (int)stack.Count;
            var from = new SlotRef(bag.Id, (ushort)slot);
            for (var pass = 0; pass < 2; pass++)
            {
                for (var c = 0; c < chests.Count && remaining > 0; c++)
                {
                    if (chests[c].Holds(stack.Type))
                    {
                        remaining -= Fill(from, chests[c], stack.Type, remaining, def.MaxStack, stacks: pass == 0);
                    }
                }
            }
        }

        if (_components.Count == 0)
        {
            return anyPending ? RejectReason.InventoryFull : RejectReason.NotEnoughItems;
        }

        operation = _components.Count == 1 ? _components[0] : new CompoundOp(_components.ToArray());
        return RejectReason.None;
    }

    /// <summary>Moves up to <paramref name="count"/> items into the stacks (or the empty slots) of one chest copy; returns how many moved.</summary>
    private int Fill(SlotRef from, ChestCopy chest, ItemTypeId type, int count, ushort limit, bool stacks)
    {
        var remaining = count;
        for (var i = 0; i < chest.Slots.Length && remaining > 0 && _components.Count < ProtocolConstants.MaxCompoundComponents; i++)
        {
            var room = stacks ? (chest.Slots[i].Type == type ? limit - chest.Slots[i].Count : 0) : (chest.Slots[i].IsEmpty ? (int)limit : 0);
            if (room <= 0)
            {
                continue;
            }

            var moved = Math.Min(room, remaining);
            _components.Add(new ItemTransferOp(from, new SlotRef(chest.Id, (ushort)i), type, (ushort)moved, limit));
            chest.Slots[i] = new ItemStack(type, (ushort)(chest.Slots[i].Count + moved));
            remaining -= moved;
        }

        return count - remaining;
    }

    /// <summary>
    /// Take all (D17): the slots of the chest ascending, each stack placed like a mined one: stacks of the hotbar, stacks of the bag, empty
    /// slots of the bag, empty slots of the hotbar. What does not fit stays in the chest.
    /// </summary>
    private RejectReason ResolveTakeAll(PlayerId player, TakeAllIntent intent, out Operation operation)
    {
        operation = null!;
        if (survival is null || !state.Containers.TryGet(intent.Container, out var chest)
            || !state.Containers.TryGetOfPlayer(player, ContainerKind.Hotbar, out var hotbar)
            || !state.Containers.TryGetOfPlayer(player, ContainerKind.PlayerBag, out var bag))
        {
            return RejectReason.InvalidParameters;
        }

        var chestHasItems = false;
        for (var slot = 0; slot < chest.Size && !chestHasItems; slot++)
        {
            chestHasItems = !chest.GetSlot(slot).IsEmpty;
        }

        if (!chestHasItems)
        {
            return RejectReason.NotEnoughItems;
        }

        var hotbarSlots = hotbar.Slots.ToArray();
        var bagSlots = bag.Slots.ToArray();
        _components.Clear();
        var anyItems = false;
        for (var slot = 0; slot < chest.Size && _components.Count < ProtocolConstants.MaxCompoundComponents; slot++)
        {
            var stack = chest.GetSlot(slot);
            if (stack.IsEmpty || !TryGetDef(stack.Type, out var def))
            {
                continue;
            }

            anyItems = true;
            var from = new SlotRef(chest.Id, (ushort)slot);
            var remaining = (int)stack.Count;
            remaining -= Place(from, hotbar.Id, hotbarSlots, stack.Type, remaining, def.MaxStack, stacks: true);
            remaining -= Place(from, bag.Id, bagSlots, stack.Type, remaining, def.MaxStack, stacks: true);
            remaining -= Place(from, bag.Id, bagSlots, stack.Type, remaining, def.MaxStack, stacks: false);
            Place(from, hotbar.Id, hotbarSlots, stack.Type, remaining, def.MaxStack, stacks: false);
        }

        if (_components.Count == 0)
        {
            return anyItems ? RejectReason.InventoryFull : RejectReason.NotEnoughItems;
        }

        operation = _components.Count == 1 ? _components[0] : new CompoundOp(_components.ToArray());
        return RejectReason.None;
    }

    private int Place(SlotRef from, ContainerId target, ItemStack[] slots, ItemTypeId type, int count, ushort limit, bool stacks)
    {
        var remaining = count;
        for (var i = 0; i < slots.Length && remaining > 0 && _components.Count < ProtocolConstants.MaxCompoundComponents; i++)
        {
            var room = stacks ? (slots[i].Type == type ? limit - slots[i].Count : 0) : (slots[i].IsEmpty ? (int)limit : 0);
            if (room <= 0)
            {
                continue;
            }

            var moved = Math.Min(room, remaining);
            _components.Add(new ItemTransferOp(from, new SlotRef(target, (ushort)i), type, (ushort)moved, limit));
            slots[i] = new ItemStack(type, (ushort)(slots[i].Count + moved));
            remaining -= moved;
        }

        return count - remaining;
    }

    private bool TryGetDef(ItemTypeId type, out ItemDef def)
    {
        def = null!;
        return survival is not null && state.ItemTypes.TryGetKey(type, out var key) && survival.Items.TryGet(key, out def!);
    }

    /// <summary>A copy of the slots of a chest that the quick stack fills while it plans.</summary>
    private readonly record struct ChestCopy(ContainerId Id, ItemStack[] Slots, float Meters)
    {
        public bool Holds(ItemTypeId type)
        {
            for (var i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].Type == type)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
