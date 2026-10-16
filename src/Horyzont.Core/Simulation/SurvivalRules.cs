using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Survival rules of the simulation when it has content (K6-29W D8): validation and resolution of item intents, the mining yield and
/// the fill cost. Reads content and the terrain (validator and resolver only, ZASADY §4); the operations it builds carry every content
/// value they depend on. Chests are reachable for item moves (K6-30 D9): a shared one or the player's own, within
/// <see cref="GameRules.ContainerKeepRangeMeters"/> of the eye. Order of the checks: docs/plans/K6-29W-ekwipunek-i-wydobycie.md, "Walidacja i rozwiązanie intencji przedmiotów".
/// </summary>
internal sealed class SurvivalRules(WorldState state, ITerrainService terrain, SurvivalContent content, GameRules rules, PropField? props = null)
{
    private const int MaterialCount = 256;

    private readonly int[] _solidByMaterial = new int[MaterialCount];
    private readonly ItemStack[] _hotbar = new ItemStack[ProtocolConstants.MaxContainerSlots];
    private readonly ItemStack[] _bag = new ItemStack[ProtocolConstants.MaxContainerSlots];
    private readonly List<Operation> _components = [];
    private readonly YieldLedger _ledger = new();
    private readonly List<PropInstance> _propsInSphere = [];

    /// <summary>
    /// The parameter step of an item intent: zero numbers, slots outside the container, the equipment container and a target equal to the
    /// source give <see cref="RejectReason.InvalidParameters"/>; a container that is neither the player's own nor a chest the player may use
    /// gives <see cref="RejectReason.ContainerLocked"/>, a chest beyond the keep range <see cref="RejectReason.OutOfReach"/>.
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

                var source = CheckSlot(player, move.From, allowChests: true, out _);
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

                    return CheckContainer(player, move.To.Container, allowChests: true, out _);
                }

                var target = CheckSlot(player, move.To.SlotRef, allowChests: true, out _);
                if (target != RejectReason.None)
                {
                    return target;
                }

                return move.To.SlotRef == move.From ? RejectReason.InvalidParameters : RejectReason.None;

            case ItemDropIntent drop:
                return drop.Count == 0 ? RejectReason.InvalidParameters : CheckSlot(player, drop.Slot, allowChests: false, out _);

            case QuickStackIntent:
                return RejectReason.None;

            case TakeAllIntent takeAll:
                if (!state.Containers.TryGet(takeAll.Container, out var chest))
                {
                    return RejectReason.ContainerLocked;
                }

                if (chest.Kind != ContainerKind.Chest || chest.Host.Kind != ContainerHostKind.ConstructBlock)
                {
                    // The own bag and hotbar are not something to take all from; the containers of others are locked.
                    return chest.Owner == player && chest.Host.Kind == ContainerHostKind.Player ? RejectReason.InvalidParameters : RejectReason.ContainerLocked;
                }

                return CheckChest(player, chest);

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
        if (move.To.AnySlot)
        {
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

        if (move.Count != source.Count)
        {
            return RejectReason.InvalidParameters;
        }

        operation = new ItemSwapOp(move.From, move.To.SlotRef, source.Type, target.Type);
        return RejectReason.None;
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
            // Nothing measurable (no terrain, area not loaded) or no containers: a plain edit as in K2, plus the props the edit removes.
            if (!measured || !hasContainers)
            {
                AddPropHarvests(edit);
                return Compose(edit, out operation);
            }

            // The tool tier of the player must reach every material that yields something (K6-31 D9); only with the content of props (D21).
            if (props is not null && !ToolsReach(player))
            {
                return RejectReason.ToolTierTooLow;
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

    private RejectReason CheckSlot(PlayerId player, SlotRef slot, bool allowChests, out Container? container)
    {
        var reason = CheckContainer(player, slot.Container, allowChests, out container);
        if (reason != RejectReason.None)
        {
            return reason;
        }

        return slot.Slot < container!.Size ? RejectReason.None : RejectReason.InvalidParameters;
    }

    /// <summary>
    /// The player's own bag and hotbar and, when <paramref name="allowChests"/>, a chest block's container the player may use (K6-30 D9);
    /// the equipment container waits for K6-33/K6-34 (D6).
    /// </summary>
    private RejectReason CheckContainer(PlayerId player, ContainerId id, bool allowChests, out Container? container)
    {
        if (!state.Containers.TryGet(id, out container))
        {
            return RejectReason.ContainerLocked;
        }

        if (container.Kind == ContainerKind.Chest && container.Host.Kind == ContainerHostKind.ConstructBlock)
        {
            return allowChests ? CheckChest(player, container) : RejectReason.ContainerLocked;
        }

        if (container.Owner != player || container.Host.Kind != ContainerHostKind.Player || container.Host.PlayerId != player)
        {
            return RejectReason.ContainerLocked;
        }

        return container.Kind is ContainerKind.PlayerBag or ContainerKind.Hotbar ? RejectReason.None : RejectReason.InvalidParameters;
    }

    /// <summary>A shared chest or the player's own, with the eye within <see cref="GameRules.ContainerKeepRangeMeters"/> of its block.</summary>
    private RejectReason CheckChest(PlayerId player, Container chest)
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
