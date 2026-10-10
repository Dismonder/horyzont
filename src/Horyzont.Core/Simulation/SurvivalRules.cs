using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Survival rules of the simulation when it has content (K6-29W D8): validation and resolution of item intents, the mining yield and
/// the fill cost. Reads content and the terrain (validator and resolver only, ZASADY §4); the operations it builds carry every content
/// value they depend on. Order of the checks: docs/plans/K6-29W-ekwipunek-i-wydobycie.md, "Walidacja i rozwiązanie intencji przedmiotów".
/// </summary>
internal sealed class SurvivalRules(WorldState state, ITerrainService terrain, SurvivalContent content)
{
    private const int MaterialCount = 256;

    private readonly int[] _solidByMaterial = new int[MaterialCount];
    private readonly ItemStack[] _hotbar = new ItemStack[ProtocolConstants.MaxContainerSlots];
    private readonly ItemStack[] _bag = new ItemStack[ProtocolConstants.MaxContainerSlots];
    private readonly List<Operation> _components = [];
    private readonly YieldLedger _ledger = new();

    /// <summary>
    /// The parameter step of an item intent: zero numbers, slots outside the container, the equipment container and a target equal to the
    /// source give <see cref="RejectReason.InvalidParameters"/>; a container that is not the player's own gives
    /// <see cref="RejectReason.ContainerLocked"/>.
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

                var source = CheckSlot(player, move.From, out _);
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

                    return CheckContainer(player, move.To.Container, out _);
                }

                var target = CheckSlot(player, move.To.SlotRef, out _);
                if (target != RejectReason.None)
                {
                    return target;
                }

                return move.To.SlotRef == move.From ? RejectReason.InvalidParameters : RejectReason.None;

            case ItemDropIntent drop:
                return drop.Count == 0 ? RejectReason.InvalidParameters : CheckSlot(player, drop.Slot, out _);

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
            // Nothing measurable (no terrain, area not loaded) or no containers: a plain edit as in K2.
            if (!measured || !hasContainers)
            {
                return RejectReason.None;
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

        var need = MiningYield.Amount(air, voxelVolume, fillYield.ItemsPerCubicMeter) - _ledger.Credit(player, edit.MaterialId);
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

        return Compose(edit, out operation);
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

    private RejectReason CheckSlot(PlayerId player, SlotRef slot, out Container? container)
    {
        var reason = CheckContainer(player, slot.Container, out container);
        if (reason != RejectReason.None)
        {
            return reason;
        }

        return slot.Slot < container!.Size ? RejectReason.None : RejectReason.InvalidParameters;
    }

    /// <summary>Only the player's own bag and hotbar; the equipment container waits for K6-33/K6-34 (D6).</summary>
    private RejectReason CheckContainer(PlayerId player, ContainerId id, out Container? container)
    {
        if (!state.Containers.TryGet(id, out container) || container.Owner != player || container.Host.Kind != ContainerHostKind.Player
            || container.Host.PlayerId != player)
        {
            return RejectReason.ContainerLocked;
        }

        return container.Kind is ContainerKind.PlayerBag or ContainerKind.Hotbar ? RejectReason.None : RejectReason.InvalidParameters;
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
