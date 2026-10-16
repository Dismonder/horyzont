using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>
/// K6-30: chests do not break the conservation of items. Random sequences of placing and removing chests, moving items to and from them and
/// compounds that fail at a random position, the removal of a chest state alone (refused: it would leave the container hosted by a block
/// without a state) and the removal of the state followed by the container in one compound (accepted: the transient state is allowed), and a chest container created or
/// removed alone (refused: the state and the container come and go together);
/// after every operation the sum of every item type equals created minus destroyed, no container hosted by a block outlives the block,
/// no block state outlives the block, every hosted chest container has its chest state, and a refused operation leaves the hash unchanged.
/// </summary>
public sealed class FunctionalBlockConservationProperties
{
    private const int Sequences = 2000;
    private const int OperationsPerSequence = 50;

    private static readonly CellPos[] _cells = [FunctionalBlocks.FreeCell, FunctionalBlocks.FreeCell2, new(7, 1, 0), new(8, 1, 0)];

    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);

    private enum Action
    {
        PlaceChest,
        PlaceChestWithItems,
        RemoveChest,
        FillBag,
        FillChest,
        BagToChest,
        ChestToBag,
        DestroyInChest,
        ClearBlock,
        DropChestState,
        DropChestStateThenContainer,
        CreateChestContainerOnly,
        RemoveChestContainerOnly,
    }

    private static Gen<Step> StepGen { get; } =
        from action in Gen.Frequency(
            (4, Gen.Constant(Action.PlaceChest)),
            (2, Gen.Constant(Action.PlaceChestWithItems)),
            (3, Gen.Constant(Action.RemoveChest)),
            (3, Gen.Constant(Action.FillBag)),
            (2, Gen.Constant(Action.FillChest)),
            (4, Gen.Constant(Action.BagToChest)),
            (3, Gen.Constant(Action.ChestToBag)),
            (1, Gen.Constant(Action.DestroyInChest)),
            (1, Gen.Constant(Action.ClearBlock)),
            (2, Gen.Constant(Action.DropChestState)),
            (2, Gen.Constant(Action.DropChestStateThenContainer)),
            (1, Gen.Constant(Action.CreateChestContainerOnly)),
            (1, Gen.Constant(Action.RemoveChestContainerOnly)))
        from cell in Gen.Choose(0, _cells.Length - 1)
        from slot in Gen.Choose(0, 3)
        from count in Gen.Choose(1, 6)
        from broken in Gen.Choose(0, 3)
        select new Step(action, cell, slot, count, broken);

    private static Gen<Step[]> Sequence { get; } = StepGen.ArrayOf(OperationsPerSequence);

    [Fact]
    public void RandomSequences_ConserveItems_NoOrphansAndRefusedOperationsChangeNothing()
    {
        Kit.ForAll(Sequence, Run, Sequences);

        // A property over sequences in which every placement is refused holds trivially: the generator must also get past the checks.
        AssertGeneratorCoverage();
    }

    [Fact]
    public void Generator_ProducesAcceptedPlacementsAndRemovalsAndRefusedOperations() => AssertGeneratorCoverage();

    private static void AssertGeneratorCoverage()
    {
        var placed = 0;
        var removed = 0;
        var stateDropped = 0;
        var refused = 0;
        foreach (var sequence in Sequence.Sample(400, 20))
        {
            var f = new FunctionalBlockFixture(players: 2);
            var applier = new OperationApplier(f.State, f.Sim.Terrain);
            foreach (var step in sequence)
            {
                var op = Build(f, step);
                var accepted = applier.TryApply(f.State.LastOpId.Next, f.State.CurrentTick, null, op, out _, out _, out _);
                if (op is ContainerCreateOp or ContainerRemoveOp)
                {
                    Assert.False(accepted, $"{op.GetType().Name} of a chest alone was accepted");
                }

                if (!accepted)
                {
                    refused++;
                }
                else if (op is CompoundOp compound && compound.Components.Any(c => c is BlockEntitySetOp { State: not null }))
                {
                    placed++;
                }
                else if (op is CompoundOp { Components: [BlockEntitySetOp { State: null }, ContainerRemoveOp] })
                {
                    stateDropped++;
                }
                else if (op is CompoundOp removal && removal.Components.Any(c => c is ContainerRemoveOp))
                {
                    removed++;
                }
            }
        }

        Assert.True(placed >= 10, $"placed {placed} times");
        Assert.True(removed >= 10, $"removed {removed} times");
        Assert.True(stateDropped >= 10, $"dropped a chest state and its container {stateDropped} times");
        Assert.True(refused >= 10, $"refused {refused} times");
    }

    private static void Run(Step[] sequence)
    {
        var f = new FunctionalBlockFixture(players: 2);
        var applier = new OperationApplier(f.State, f.Sim.Terrain);
        var expected = new long[SurvivalContentFactory.ItemKeys.Count + 1];
        foreach (var step in sequence)
        {
            var op = Build(f, step);
            var hash = StateHasher.Hash(f.State);
            if (applier.TryApply(f.State.LastOpId.Next, f.State.CurrentTick, null, op, out _, out _, out _))
            {
                Account(op, expected);
            }
            else
            {
                Assert.Equal(hash, StateHasher.Hash(f.State));
            }

            Assert.Equal(expected, Totals(f));
            AssertNoOrphans(f);
        }
    }

    /// <summary>The operation of a step against the current state; chest steps on cells without a chest become operations that are refused.</summary>
    private static Operation Build(FunctionalBlockFixture f, Step step)
    {
        var cell = _cells[step.Cell];
        var chest = ChestAt(f, cell);
        var chestId = chest ?? new ContainerId(9999);
        var bagSlot = new SlotRef(Bag, (ushort)step.Slot);
        var chestSlot = new SlotRef(chestId, (ushort)step.Slot);
        switch (step.Action)
        {
            case Action.PlaceChest:
                return CompoundConstructTests.PlaceChestCompound(f.State.ContainerIds.PeekNext(), cell);
            case Action.PlaceChestWithItems:
                var id = f.State.ContainerIds.PeekNext();
                Operation[] parts =
                [
                    ItemCreate(bagSlot, 2),
                    ChestContainer(id, ConstructId, cell, 8),
                    SetChestState(cell, id),
                    PlaceBlock(cell, FunctionalBlocks.ChestType),
                ];

                // Broken 0..2 puts a destroy of more than a stack in front of the block (the compound fails there), 3 keeps it valid.
                return new CompoundOp(step.Broken < 3 ? Insert(parts, step.Broken, new ItemDestroyOp(bagSlot, _stone, 501, ItemSink.Craft)) : parts);
            case Action.RemoveChest:
                return new CompoundOp(
                [
                    new ContainerRemoveOp(chestId),
                    ClearState(cell, FunctionalBlocks.ChestType),
                    new BlockClearOp(ConstructId, cell),
                ]);
            case Action.FillBag:
                return ItemCreate(bagSlot, step.Count);
            case Action.FillChest:
                return ItemCreate(chestSlot, step.Count);
            case Action.BagToChest:
                return new ItemTransferOp(bagSlot, chestSlot, _stone, (ushort)step.Count, 500);
            case Action.ChestToBag:
                return new ItemTransferOp(chestSlot, bagSlot, _stone, (ushort)step.Count, 500);
            case Action.DestroyInChest:
                return new ItemDestroyOp(chestSlot, _stone, (ushort)step.Count, ItemSink.Craft);
            case Action.DropChestState:
                // The state alone: refused while its container exists (a chest container needs its state at the end of every operation).
                return ClearState(cell, FunctionalBlocks.ChestType);
            case Action.DropChestStateThenContainer:
                return new CompoundOp([ClearState(cell, FunctionalBlocks.ChestType), new ContainerRemoveOp(chestId)]);
            case Action.CreateChestContainerOnly:
                // The container alone: refused while no chest state refers to it (the checkpoint would not load such a world).
                return ChestContainer(f.State.ContainerIds.PeekNext(), ConstructId, cell, 4);
            case Action.RemoveChestContainerOnly:
                // The container alone: refused while its chest state stays.
                return new ContainerRemoveOp(chestId);
            default:
                return new BlockClearOp(ConstructId, cell);
        }
    }

    private static ItemCreateOp ItemCreate(SlotRef slot, int count) => new(slot, _stone, (ushort)count, 500, ItemSource.Harvest);

    private static Operation[] Insert(Operation[] parts, int index, Operation extra) => [.. parts[..index], extra, .. parts[index..]];

    private static ContainerId? ChestAt(FunctionalBlockFixture f, CellPos cell) =>
        f.State.BlockEntities.TryGet(new BlockRef(ConstructId, cell), out var state) && state is ChestBlockState chest ? chest.Container : null;

    private static void Account(Operation op, long[] expected)
    {
        switch (op)
        {
            case ItemCreateOp create:
                expected[create.Type.Value] += create.Count;
                break;
            case ItemDestroyOp destroy:
                expected[destroy.Type.Value] -= destroy.Count;
                break;
            case CompoundOp compound:
                foreach (var component in compound.Components)
                {
                    Account(component, expected);
                }

                break;
        }
    }

    private static long[] Totals(FunctionalBlockFixture f)
    {
        var totals = new long[SurvivalContentFactory.ItemKeys.Count + 1];
        foreach (var container in f.State.Containers.All)
        {
            foreach (var stack in container.Slots)
            {
                if (!stack.IsEmpty)
                {
                    Assert.InRange(stack.Count, (ushort)1, (ushort)500);
                    totals[stack.Type.Value] += stack.Count;
                }
            }
        }

        return totals;
    }

    /// <summary>No container hosted by a block without the block, no block state without the block, a chest state and its container agree.</summary>
    private static void AssertNoOrphans(FunctionalBlockFixture f)
    {
        foreach (var container in f.State.Containers.All.Where(c => c.Host.Kind == ContainerHostKind.ConstructBlock))
        {
            Assert.True(f.State.Constructs.TryGet(new EntityId(container.Host.Id), out var construct), $"container {container.Id.Value} outlived its construct");
            Assert.False(construct.GetBlock(container.Host.Cell).IsEmpty, $"container {container.Id.Value} outlived its block");
        }

        foreach (var cell in _cells)
        {
            if (f.State.BlockEntities.TryGet(new BlockRef(ConstructId, cell), out var state))
            {
                Assert.False(f.BlockAt(cell).IsEmpty, $"state of {cell} outlived its block");
                var chest = Assert.IsType<ChestBlockState>(state);
                Assert.Equal(ContainerHost.ConstructBlock(ConstructId, cell), f.Container(chest.Container).Host);
            }
            else
            {
                Assert.DoesNotContain(f.HostedBy(cell), c => c.Kind == ContainerKind.Chest);
            }
        }
    }

    private readonly record struct Step(Action Action, int Cell, int Slot, int Count, int Broken);
}
