using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Survival rules of the simulation when it has content (K6-29W D8): validation and resolution of item intents, the mining yield and
/// the fill cost. Reads content and the terrain (validator and resolver only, ZASADY §4); the operations it builds carry every content
/// value they depend on. Chests are reachable for item moves (K6-30 D9): a shared one or the player's own, within
/// <see cref="GameRules.ContainerKeepRangeMeters"/> of the eye. Order of the checks: docs/plans/K6-29W-ekwipunek-i-wydobycie.md, "Walidacja i rozwiązanie intencji przedmiotów".
/// </summary>
internal sealed class SurvivalRules(
    WorldState state, ITerrainService terrain, SurvivalContent content, GameRules rules, PropField? props, VitalsRules vitals, HazardRules hazards)
{
    private const int MaterialCount = 256;

    private readonly int[] _solidByMaterial = new int[MaterialCount];
    private readonly ItemStack[] _hotbar = new ItemStack[ProtocolConstants.MaxContainerSlots];
    private readonly ItemStack[] _bag = new ItemStack[ProtocolConstants.MaxContainerSlots];
    private readonly List<Operation> _components = [];
    private readonly YieldLedger _ledger = new();
    private readonly List<PropInstance> _propsInSphere = [];
    private readonly FoodRules _food = new(state, content, rules, vitals ?? throw new ArgumentNullException(nameof(vitals)));
    private readonly SuitRules? _suit = content.HasHazards ? new SuitRules(state, content, hazards ?? throw new ArgumentNullException(nameof(hazards))) : null;

    /// <summary>
    /// The parameter step of an item intent: zero numbers, slots outside the container, the equipment container and a target equal to the
    /// source give <see cref="RejectReason.InvalidParameters"/>; a container that is neither the player's own nor a chest the player may use
    /// gives <see cref="RejectReason.ContainerLocked"/>, a chest beyond the keep range <see cref="RejectReason.OutOfReach"/>. With the hazard content (K6-34 D13) the own equipment container is a valid
    /// source and target of a move and a valid source of a drop; eating and refilling take from the own bag and hotbar only.
    /// </summary>
    internal RejectReason CheckItemParameters(PlayerId player, Intent intent)
    {
        switch (intent)
        {
            case ItemMoveIntent move:
                if (move.Count == 0 || move.ExpectedCount == 0 || move.ExpectedType.IsNone)
                {
                    return RejectReason.InvalidParameters;
                }

                var source = CheckSlot(player, move.From, allowChests: true, out _, content.HasHazards);
                if (source != RejectReason.None)
                {
                    return source;
                }

                if (move.To.AnySlot)
                {
                    if (move.To.Slot != 0 || move.To.Container == move.From.Container)
                    {
                        return RejectReason.InvalidParameters;
                    }

                    var anyTarget = CheckContainer(player, move.To.Container, allowChests: true, out var anyContainer, content.HasHazards);
                    return anyTarget == RejectReason.None && anyContainer!.Kind is ContainerKind.StationOutput or ContainerKind.Tombstone ? RejectReason.InvalidParameters : anyTarget;
                }

                var target = CheckSlot(player, move.To.SlotRef, allowChests: true, out var targetContainer, content.HasHazards);
                if (target != RejectReason.None)
                {
                    return target;
                }

                // The output of a station is taken from, never put into (K6-32 D17); so is a tombstone (K6-35 D13).
                if (targetContainer!.Kind is ContainerKind.StationOutput or ContainerKind.Tombstone)
                {
                    return RejectReason.InvalidParameters;
                }

                return move.To.SlotRef == move.From ? RejectReason.InvalidParameters : RejectReason.None;

            case ItemDropIntent drop:
                return drop.Count == 0 ? RejectReason.InvalidParameters : CheckSlot(player, drop.Slot, allowChests: false, out _, content.HasHazards);

            case QuickStackIntent:
                return RejectReason.None;

            case EatIntent eat:
                // Eating is from the own bag and hotbar only (K6-33 D10, DO USTALENIA-6); anything else, a missing container or a slot outside it is a parameter error.
                return IsOwnBagOrHotbarSlot(player, eat.Container, eat.Slot) ? RejectReason.None : RejectReason.InvalidParameters;

            case SuitRefillIntent refill:
                // The same source as eating (K6-34 D7), and only with the hazard content.
                return content.HasHazards && IsOwnBagOrHotbarSlot(player, refill.Container, refill.Slot) ? RejectReason.None : RejectReason.InvalidParameters;

            case TakeAllIntent takeAll:
                if (!state.Containers.TryGet(takeAll.Container, out var chest))
                {
                    return RejectReason.ContainerLocked;
                }

                if (TombstoneRecovery.IsTombstone(chest))
                {
                    return CheckTombstone(player, chest);
                }

                if (!IsBlockContainer(chest))
                {
                    // The own bag and hotbar are not something to take all from; the containers of others are locked.
                    return chest.Owner == player && chest.Host.Kind == ContainerHostKind.Player ? RejectReason.InvalidParameters : RejectReason.ContainerLocked;
                }

                return CheckBlockContainer(player, chest);

            default:
                return RejectReason.InvalidParameters;
        }
    }

    /// <summary>
    /// Resolves a validated intent into an operation, or rejects it when the items do not allow it. Stages ledger changes that
    /// <see cref="Commit"/> applies once the operation was applied.
    /// </summary>
    internal RejectReason Resolve(PlayerId player, Intent intent, IntentFacts facts, out Operation operation)
    {
        _ledger.Begin(player);
        switch (intent)
        {
            case ItemMoveIntent move:
                return ResolveMove(move, out operation);
            case ItemDropIntent drop:
                return ResolveDrop(drop, out operation);
            case EatIntent eat:
                return _food.Resolve(player, eat, out operation);
            case SuitRefillIntent refill:
                operation = null!;
                return _suit is null ? RejectReason.InvalidParameters : _suit.Resolve(player, refill, out operation);
            case TerrainToolIntent:
                return ResolveTerrainEdit(player, (TerrainEditOp)IntentResolver.Resolve(state, player, intent, facts), out operation);
            default:
                operation = IntentResolver.Resolve(state, player, intent, facts);
                return RejectReason.None;
        }
    }

    /// <summary>Applies the ledger changes of the last <see cref="Resolve"/>; called only after its operation was applied.</summary>
    internal void Commit() => _ledger.Commit();

    private RejectReason ResolveMove(ItemMoveIntent move, out Operation operation)
    {
        var reason = ResolveMoveCore(move, out operation);

        // What empties a tombstone takes it away in the same operation (K6-35 D13).
        if (reason == RejectReason.None)
        {
            operation = TombstoneRecovery.CompleteRemoval(state, operation);
        }

        return reason;
    }

    private RejectReason ResolveMoveCore(ItemMoveIntent move, out Operation operation)
    {
        operation = null!;
        GetContainer(move.From.Container, out var sourceContainer);
        var source = sourceContainer.GetSlot(move.From.Slot);
        if (source.Type != move.ExpectedType || source.Count != move.ExpectedCount)
        {
            return RejectReason.ContainerChanged;
        }

        if (move.Count > source.Count)
        {
            return RejectReason.NotEnoughItems;
        }

        if (!TryGetDef(source.Type, out var def))
        {
            return RejectReason.InvalidParameters;
        }

        var limit = def.MaxStack;
        GetContainer(move.To.Container, out var targetContainer);
        var toEquipment = targetContainer.Kind == ContainerKind.Equipment;
        if (move.To.AnySlot)
        {
            if (toEquipment)
            {
                return ResolveMoveIntoEquipment(move, def, targetContainer, out operation);
            }

            var slots = Copy(targetContainer, _bag);
            _components.Clear();
            if (ItemPlacement.MoveAny(move.From, targetContainer.Id, slots, source.Type, move.Count, limit, _components) == 0)
            {
                return RejectReason.InventoryFull;
            }

            operation = new CompoundOp(_components.ToArray());
            return RejectReason.None;
        }

        var target = targetContainer.GetSlot(move.To.Slot);
        if (toEquipment)
        {
            // One item to a slot of the layout that accepts it; a stack in the equipment is never larger than one (D13).
            if (move.Count != 1 || !EquipmentLayout.Accepts(move.To.Slot, def.EquipmentSlot))
            {
                return RejectReason.InvalidParameters;
            }

            limit = 1;
        }

        if (target.IsEmpty || target.Type == source.Type)
        {
            var moved = Math.Min(move.Count, limit - target.Count);
            if (moved <= 0)
            {
                return RejectReason.InventoryFull;
            }

            operation = new ItemTransferOp(move.From, move.To.SlotRef, source.Type, (ushort)moved, limit);
            return RejectReason.None;
        }

        // A swap also puts an item into the container it takes from: not with the output of a station, and not with a tombstone (K6-35 D13).
        if (sourceContainer.Kind is ContainerKind.StationOutput or ContainerKind.Tombstone || move.Count != source.Count)
        {
            return RejectReason.InvalidParameters;
        }

        // The item of the target comes to the slot of the source: a slot of the equipment takes only what its place in the layout accepts (D13).
        if (sourceContainer.Kind == ContainerKind.Equipment && !(TryGetDef(target.Type, out var targetDef) && EquipmentLayout.Accepts(move.From.Slot, targetDef.EquipmentSlot)))
        {
            return RejectReason.InvalidParameters;
        }

        operation = new ItemSwapOp(move.From, move.To.SlotRef, source.Type, target.Type);
        return RejectReason.None;
    }

    /// <summary>A move to "any slot" of the own equipment: one item into the first empty slot of the layout that accepts it (D13); all such slots taken give <see cref="RejectReason.InventoryFull"/>, no slot that accepts it <see cref="RejectReason.InvalidParameters"/>.</summary>
    private static RejectReason ResolveMoveIntoEquipment(ItemMoveIntent move, ItemDef def, Container equipment, out Operation operation)
    {
        operation = null!;
        if (move.Count != 1)
        {
            return RejectReason.InvalidParameters;
        }

        var fits = false;
        for (var i = 0; i < equipment.Size; i++)
        {
            if (!EquipmentLayout.Accepts(i, def.EquipmentSlot))
            {
                continue;
            }

            fits = true;
            if (equipment.GetSlot(i).IsEmpty)
            {
                operation = new ItemTransferOp(move.From, new SlotRef(equipment.Id, (ushort)i), move.ExpectedType, 1, 1);
                return RejectReason.None;
            }
        }

        // An item that no slot of the layout takes is not "full" but "does not fit" (review B5, P3-4).
        return fits ? RejectReason.InventoryFull : RejectReason.InvalidParameters;
    }

    private RejectReason ResolveDrop(ItemDropIntent drop, out Operation operation)
    {
        operation = null!;
        GetContainer(drop.Slot.Container, out var container);
        var stack = container.GetSlot(drop.Slot.Slot);
        if (stack.Type != drop.ExpectedType || stack.Count != drop.ExpectedCount)
        {
            return RejectReason.ContainerChanged;
        }

        if (stack.IsEmpty || drop.Count > stack.Count)
        {
            return RejectReason.NotEnoughItems;
        }

        // Until dropped items exist a tool could not be recovered (D13).
        if (!TryGetDef(stack.Type, out var def) || (def.Flags & ItemFlags.Tool) != 0)
        {
            return RejectReason.InvalidParameters;
        }

        operation = new ItemDestroyOp(drop.Slot, stack.Type, drop.Count, ItemSink.Despawn);
        return RejectReason.None;
    }

    private RejectReason ResolveTerrainEdit(PlayerId player, TerrainEditOp edit, out Operation operation)
    {
        operation = edit;
        if (!state.Planets.TryGet(edit.Planet, out var planet))
        {
            return RejectReason.UnknownPlanet;
        }

        var voxel = (double)planet.VoxelSizeMeters;
        var voxelVolume = voxel * voxel * voxel;
        var measured = terrain.MeasureSphere(edit.Planet, edit.Center, edit.Radius, _solidByMaterial, out var air);
        var hasContainers = state.Containers.TryGetOfPlayer(player, ContainerKind.Hotbar, out var hotbarContainer)
            & state.Containers.TryGetOfPlayer(player, ContainerKind.PlayerBag, out var bagContainer);
        _components.Clear();
        if (edit.Mode == TerrainEditMode.Remove)
        {
            // The tool tier of the player must reach every material that yields something (K6-31 D9); only with the content of props (D21).
            // It does not depend on having containers for the yield. Nothing is checked when nothing was measured: the validator refuses an
            // area that is not loaded before the resolver runs (the applier does not look at the terrain), so this is a terrain the service
            // cannot read at all.
            if (measured && props is not null && !ToolsReach(player))
            {
                return RejectReason.ToolTierTooLow;
            }

            // Nothing measurable or no containers: a plain edit as in K2, plus the props the edit removes.
            if (!measured || !hasContainers)
            {
                AddPropHarvests(edit);
                return Compose(edit, out operation);
            }

            var hotbar = Copy(hotbarContainer!, _hotbar);
            var bag = Copy(bagContainer!, _bag);
            for (var material = 1; material < MaterialCount; material++)
            {
                var solid = _solidByMaterial[material];
                if (solid <= 0 || !TryGetYieldItem((byte)material, out var yield, out var type, out var def))
                {
                    continue;
                }

                var amount = MiningYield.Amount(solid, voxelVolume, yield.ItemsPerCubicMeter) + _ledger.Remainder(player, (byte)material);
                var whole = MiningYield.WholeItems(amount, out var remainder);
                _ledger.StageRemainder((byte)material, remainder);

                // What does not fit is lost until dropped items exist (D11); digging is never blocked.
                ItemPlacement.Create(hotbarContainer!.Id, hotbar, bagContainer!.Id, bag, type, whole, def.MaxStack, ItemSource.Harvest, _components);
            }

            AddPropHarvests(edit);
            return Compose(edit, out operation);
        }

        if (!measured)
        {
            return RejectReason.AreaNotLoaded;
        }

        if (!hasContainers || !TryGetYieldItem(edit.MaterialId, out var fillYield, out var fillType, out _))
        {
            return RejectReason.NotEnoughItems;
        }

        // The material write repaints the whole sphere (TerrainEditSemantics.MaterialSphereRadiusOffset is 0): the empty voxels turn
        // solid and the solid ones of another material change it, so both are paid at the fill rate. Only voxels already of the fill
        // material are free; otherwise a fill inside solid terrain would turn stone into a richer material at no cost.
        var changed = air;
        for (var material = 1; material < MaterialCount; material++)
        {
            if (material != edit.MaterialId)
            {
                changed += _solidByMaterial[material];
            }
        }

        var need = MiningYield.Amount(changed, voxelVolume, fillYield.ItemsPerCubicMeter) - _ledger.Credit(player, edit.MaterialId);
        var cost = MiningYield.Cost(need, out var credit);
        _ledger.StageCredit(edit.MaterialId, credit);
        if (cost > 0)
        {
            var bagSlots = Copy(bagContainer!, _bag);
            var hotbarSlots = Copy(hotbarContainer!, _hotbar);
            if (ItemPlacement.CountOf(bagSlots, fillType) + ItemPlacement.CountOf(hotbarSlots, fillType) < cost)
            {
                return RejectReason.NotEnoughItems;
            }

            var left = ItemPlacement.Destroy(bagContainer!.Id, bagSlots, fillType, cost, ItemSink.BuildCost, _components);
            ItemPlacement.Destroy(hotbarContainer!.Id, hotbarSlots, fillType, left, ItemSink.BuildCost, _components);
        }

        AddPropHarvests(edit);
        return Compose(edit, out operation);
    }

    /// <summary>
    /// True when the tools in the hotbar of the player reach the tier of every material with a yield in the measured sphere. A material
    /// without a yield entry has no requirement.
    /// </summary>
    private bool ToolsReach(PlayerId player)
    {
        var tier = -1;
        for (var material = 1; material < MaterialCount; material++)
        {
            if (_solidByMaterial[material] <= 0 || !state.Materials.TryGetKey((byte)material, out var key) || !content.TryGetYield(key, out var yield))
            {
                continue;
            }

            tier = tier < 0 ? ToolTiers.Best(state, content, player) : tier;
            if (yield.ToolTier > tier)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Appends a harvest, without items, of every prop that has not been harvested yet whose anchor lies in the sphere of the edit plus
    /// <see cref="GameRules.PropAnchorMarginMeters"/>: the ground under it was dug or buried (K6-31 D12). In the order of the identifiers,
    /// after the item components and before the terrain edit, which stays last.
    /// </summary>
    private void AddPropHarvests(TerrainEditOp edit)
    {
        if (props is null)
        {
            return;
        }

        _propsInSphere.Clear();
        props.CollectInSphere(edit.Planet, edit.Center, edit.Radius + rules.PropAnchorMarginMeters, _propsInSphere);
        foreach (var prop in _propsInSphere)
        {
            // The edit itself is one more component of the compound.
            if (_components.Count + 1 >= ProtocolConstants.MaxCompoundComponents)
            {
                break;
            }

            if (!state.HarvestedProps.Contains(edit.Planet, prop.Id))
            {
                _components.Add(new PropHarvestOp(edit.Planet, prop.Id, state.CurrentTick));
            }
        }
    }

    /// <summary>The edit alone when there are no item components, else a compound with the edit last (D4).</summary>
    private RejectReason Compose(TerrainEditOp edit, out Operation operation)
    {
        if (_components.Count == 0)
        {
            operation = edit;
            return RejectReason.None;
        }

        _components.Add(edit);
        operation = new CompoundOp(_components.ToArray());
        return RejectReason.None;
    }

    private RejectReason CheckSlot(PlayerId player, SlotRef slot, bool allowChests, out Container? container, bool allowEquipment = false)
    {
        var reason = CheckContainer(player, slot.Container, allowChests, out container, allowEquipment);
        if (reason != RejectReason.None)
        {
            return reason;
        }

        return slot.Slot < container!.Size ? RejectReason.None : RejectReason.InvalidParameters;
    }

    /// <summary>
    /// The player's own bag and hotbar and, when <paramref name="allowChests"/>, a chest block's container the player may use (K6-30 D9);
    /// the own equipment container when <paramref name="allowEquipment"/> (K6-34 D13: only with the hazard content, only for moves and drops).
    /// </summary>
    private RejectReason CheckContainer(PlayerId player, ContainerId id, bool allowChests, out Container? container, bool allowEquipment = false)
    {
        if (!state.Containers.TryGet(id, out container))
        {
            return RejectReason.ContainerLocked;
        }

        if (IsBlockContainer(container))
        {
            return allowChests ? CheckBlockContainer(player, container) : RejectReason.ContainerLocked;
        }

        // A tombstone of the player, in reach; the container of any other persistent entity is locked until the package that owns its rule adds one (K6-35 D13).
        if (TombstoneRecovery.IsTombstone(container))
        {
            return allowChests ? CheckTombstone(player, container) : RejectReason.ContainerLocked;
        }

        if (container.Owner != player || container.Host.Kind != ContainerHostKind.Player || container.Host.PlayerId != player)
        {
            return RejectReason.ContainerLocked;
        }

        return container.Kind is ContainerKind.PlayerBag or ContainerKind.Hotbar || (allowEquipment && container.Kind == ContainerKind.Equipment)
            ? RejectReason.None
            : RejectReason.InvalidParameters;
    }

    /// <summary>A slot of the own bag or hotbar: the source of eating and refilling.</summary>
    private bool IsOwnBagOrHotbarSlot(PlayerId player, ContainerId id, ushort slot) =>
        state.Containers.TryGet(id, out var container)
        && container.Owner == player
        && container.Kind is ContainerKind.PlayerBag or ContainerKind.Hotbar
        && slot < container.Size;

    /// <summary>True for the containers of a block: a chest and the input and the output of a station (K6-32 D17).</summary>
    private static bool IsBlockContainer(Container container) =>
        container.Host.Kind == ContainerHostKind.ConstructBlock
        && container.Kind is ContainerKind.Chest or ContainerKind.StationInput or ContainerKind.StationOutput;

    /// <summary>True when the content has recipes (K6-32): the crafting, the stations with time and the discoveries are on.</summary>
    internal bool HasRecipes => content.Recipes.Count > 0;

    /// <summary>True when the content has hazards (K6-34): the suit, the equipment and the oxygen generator are on.</summary>
    internal bool HasHazards => content.HasHazards;

    /// <summary>A shared container of a block or the player's own, with the eye within <see cref="GameRules.ContainerKeepRangeMeters"/> of its block.</summary>
    private RejectReason CheckBlockContainer(PlayerId player, Container chest)
    {
        if (chest.Access != ContainerAccess.Shared && chest.Owner != player)
        {
            return RejectReason.ContainerLocked;
        }

        return ContainerReach.TryDistanceFromEye(state, rules, player, new EntityId(chest.Host.Id), chest.Host.Cell, out var meters)
            && meters <= rules.ContainerKeepRangeMeters
            ? RejectReason.None
            : RejectReason.OutOfReach;
    }

    /// <summary>The tombstone of the player (owned and private), with the eye within <see cref="GameRules.ContainerKeepRangeMeters"/> of its entity (D13); nobody else may use it.</summary>
    private RejectReason CheckTombstone(PlayerId player, Container tombstone)
    {
        if (tombstone.Access != ContainerAccess.Private || tombstone.Owner != player)
        {
            return RejectReason.ContainerLocked;
        }

        return ContainerReach.TryDistanceToEntity(state, rules, player, new EntityId(tombstone.Host.Id), out var meters) && meters <= rules.ContainerKeepRangeMeters
            ? RejectReason.None
            : RejectReason.OutOfReach;
    }

    private void GetContainer(ContainerId id, out Container container)
    {
        if (!state.Containers.TryGet(id, out container!))
        {
            throw new InvalidOperationException($"Container {id.Value} passed validation but does not exist.");
        }
    }

    private bool TryGetDef(ItemTypeId type, out ItemDef def)
    {
        def = null!;
        return state.ItemTypes.TryGetKey(type, out var key) && content.Items.TryGet(key, out def!);
    }

    private bool TryGetYieldItem(byte material, out TerrainYieldDef yield, out ItemTypeId type, out ItemDef def)
    {
        yield = null!;
        def = null!;
        type = default;
        return state.Materials.TryGetKey(material, out var key)
            && content.TryGetYield(key, out yield!)
            && content.Items.TryGet(yield.ItemKey, out def!)
            && state.ItemTypes.TryGetId(yield.ItemKey, out type);
    }

    private static Span<ItemStack> Copy(Container container, ItemStack[] scratch)
    {
        var slots = scratch.AsSpan(0, container.Size);
        container.Slots.CopyTo(slots);
        return slots;
    }
}
