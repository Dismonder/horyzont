using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Crafting;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>
/// K6-32: crafting and stations do not break the conservation of items. Random sequences of placing and removing stations, filling bags and chests,
/// moving items to and from stations, crafting from several chests (compounds that fail at a random position), choosing recipes and settling
/// stations by <see cref="StationProcess"/>; after every operation the sum of every item type equals created minus destroyed, a station state and its
/// two containers agree, and a refused operation leaves the hash unchanged.
/// </summary>
public sealed class CraftingConservationProperties
{
    private const int Sequences = 2000;
    private const int OperationsPerSequence = 50;
    private const ulong Duration = 60;
    private const ushort GemLimit = 5;
    private const string Recipe = "t:smelt";

    private static readonly CellPos[] _stationCells = [CraftingFixture.StationCell, CraftingFixture.StationCell2];
    private static readonly CellPos[] _chestCells = [new(7, 1, 0), new(8, 1, 0), new(9, 1, 0)];

    /// <summary>2 stone and 1 soil as fuel make 1 gem.</summary>
    private static readonly StationRecipe _recipe = new(
        [new StationEntry(CraftingFixture.Stone, 2)], [new StationEntry(CraftingFixture.Soil, 1)], new StationEntry(CraftingFixture.Gem, 1));

    private enum Action
    {
        PlaceStation,
        RemoveStation,
        FillBag,
        FillChest,
        BagToInput,

        /// <summary>Puts a wsad for the recipe (stone and soil as fuel) straight into the input slots 0 and 1, so that stations get to settle pieces.</summary>
        LoadInput,
        OutputToBag,
        CraftFromChests,
        SelectRecipe,
        Settle,
        Advance,

        /// <summary>Review A1 (4-3): a standalone operation that would leave a station container without its state or the state without its containers. Always refused.</summary>
        BreakStation,
    }

    private static Gen<Step> StepGen { get; } =
        from action in Gen.Frequency(
            (3, Gen.Constant(Action.PlaceStation)),
            (2, Gen.Constant(Action.RemoveStation)),
            (4, Gen.Constant(Action.FillBag)),
            (5, Gen.Constant(Action.FillChest)),
            (4, Gen.Constant(Action.BagToInput)),
            (5, Gen.Constant(Action.LoadInput)),
            (3, Gen.Constant(Action.OutputToBag)),
            (6, Gen.Constant(Action.CraftFromChests)),
            (3, Gen.Constant(Action.SelectRecipe)),
            (5, Gen.Constant(Action.Settle)),
            (3, Gen.Constant(Action.Advance)),
            (2, Gen.Constant(Action.BreakStation)))
        from station in Gen.Choose(0, _stationCells.Length - 1)
        from chest in Gen.Choose(0, _chestCells.Length - 1)
        from slot in Gen.Choose(0, 3)
        from count in Gen.Choose(1, 6)
        from broken in Gen.Choose(0, 3)
        from soil in Gen.Choose(0, 1)
        from ticks in Gen.Choose(1, 200)
        select new Step(action, station, chest, slot, count, broken, soil == 1, ticks);

    private static Gen<Step[]> Sequence { get; } = StepGen.ArrayOf(OperationsPerSequence);

    [Fact]
    public void RandomSequences_ConserveItems_StationsAgreeWithTheirContainers_RefusedOperationsChangeNothing()
    {
        Kit.ForAll(Sequence, sequence => Run(sequence, null), Sequences);

        // A property over sequences in which every operation is refused holds trivially: the generator must also get past the checks.
        AssertGeneratorCoverage();
    }

    [Fact]
    public void Generator_ProducesAcceptedAndRefusedOperationsAndSettledPieces() => AssertGeneratorCoverage();

    private static void AssertGeneratorCoverage()
    {
        var coverage = new Coverage();
        foreach (var sequence in Sequence.Sample(400, 20))
        {
            Run(sequence, coverage);
        }

        Assert.True(coverage.Placed >= 10, $"placed {coverage.Placed} times");
        Assert.True(coverage.Removed >= 10, $"removed {coverage.Removed} times");
        Assert.True(coverage.Crafted >= 10, $"crafted {coverage.Crafted} times");
        Assert.True(coverage.PiecesSettled >= 10, $"settled {coverage.PiecesSettled} pieces");

        // Review A1 (4-14): PiecesSettled counts the ItemCreateOp components; this counts the settlements that made at least one piece (Done >= 1).
        Assert.True(coverage.SettlementsWithPieces >= 20, $"{coverage.SettlementsWithPieces} settlements with pieces");
        Assert.True(coverage.Refused >= 10, $"refused {coverage.Refused} times");
    }

    private static void Run(Step[] sequence, Coverage? coverage)
    {
        var f = new CraftingFixture(players: 2);
        var chests = _chestCells.Select(cell => f.Blocks.SetUpChestAt(cell, Player1, ContainerAccess.Shared, 8)).ToArray();
        var applier = new OperationApplier(f.State, f.Blocks.Sim.Terrain);
        var expected = new long[SurvivalContentFactory.ItemKeys.Count + 1];
        ulong now = 1000;
        var discovered = false;
        foreach (var step in sequence)
        {
            if (step.Action == Action.Advance)
            {
                now += (ulong)step.Ticks;
                continue;
            }

            var op = Build(f, chests, step, now, discovered);
            var hash = StateHasher.Hash(f.State);
            if (applier.TryApply(f.State.LastOpId.Next, f.State.CurrentTick, null, op, out _, out _, out _))
            {
                Assert.NotEqual(Action.BreakStation, step.Action);
                Account(op, expected);
                discovered |= f.State.Progress.TryGet(Player1, out var progress) && progress.DiscoveredItems.Contains(CraftingFixture.Gem);
                coverage?.Accepted(step.Action, op);
            }
            else
            {
                Assert.Equal(hash, StateHasher.Hash(f.State));
                coverage?.Refuse();
            }

            Assert.Equal(expected, Totals(f));
            AssertStationsAgree(f);
        }
    }

    private static Operation Build(CraftingFixture f, ContainerId[] chests, Step step, ulong now, bool discovered)
    {
        var cell = _stationCells[step.Station];
        var hasStation = TryStation(f, cell, out var station);
        var missing = new ContainerId(9999);
        var input = hasStation ? station!.Input : missing;
        var output = hasStation ? station!.Output : missing;
        var item = step.Soil ? CraftingFixture.Soil : CraftingFixture.Stone;
        switch (step.Action)
        {
            case Action.PlaceStation:
                return f.PlaceStation(cell);
            case Action.RemoveStation:
                return CraftingFixture.RemoveStation(cell, input, output);
            case Action.FillBag:
                return new ItemCreateOp(new SlotRef(Bag, (ushort)step.Slot), item, (ushort)step.Count, 500, ItemSource.Harvest);
            case Action.FillChest:
                return new ItemCreateOp(new SlotRef(chests[step.Chest], (ushort)step.Slot), CraftingFixture.Stone, (ushort)(step.Count * 3), 500, ItemSource.Harvest);
            case Action.BagToInput:
                return new ItemTransferOp(new SlotRef(Bag, (ushort)step.Slot), new SlotRef(input, (ushort)(2 + (step.Slot % 2))), item, (ushort)step.Count, 500);
            case Action.LoadInput:
                return new CompoundOp(
                [
                    new ItemCreateOp(new SlotRef(input, 0), CraftingFixture.Stone, (ushort)(step.Count * 2), 500, ItemSource.Admin),
                    new ItemCreateOp(new SlotRef(input, 1), CraftingFixture.Soil, (ushort)step.Count, 500, ItemSource.Admin),
                ]);
            case Action.OutputToBag:
                return new ItemTransferOp(new SlotRef(output, (ushort)(step.Slot % 2)), new SlotRef(Bag, (ushort)(10 + step.Slot)), CraftingFixture.Gem, (ushort)step.Count, GemLimit);
            case Action.CraftFromChests:
                return Craft(chests, step, discovered);
            case Action.SelectRecipe:
                return hasStation
                    ? CraftingFixture.StationState(cell, input, output, Recipe, new Tick(now))
                    : CraftingFixture.StationState(cell, missing, missing, Recipe, new Tick(now));
            case Action.BreakStation:
                return BreakStation(f, cell, hasStation, input, output, step);
            default:
                return hasStation ? Settle(f, cell, station!, now) : CraftingFixture.StationState(cell, missing, missing);
        }
    }

    /// <summary>
    /// Four variants (by <see cref="Step.Slot"/>): remove one container of a live station, clear its state keeping the containers, create a station container
    /// without a state, point the state at the input of the other station.
    /// </summary>
    private static Operation BreakStation(CraftingFixture f, CellPos cell, bool hasStation, ContainerId input, ContainerId output, Step step)
    {
        switch (step.Slot % 4)
        {
            case 0:
                return new ContainerRemoveOp(step.Soil ? output : input);
            case 1:
                return ClearState(cell, CraftingFixture.StationBlockType);
            case 2:
                var id = f.State.ContainerIds.PeekNext();
                return hasStation
                    ? CraftingFixture.StationContainer(id, ContainerKind.StationInput, cell)
                    : new CompoundOp(
                    [
                        CraftingFixture.StationContainer(id, ContainerKind.StationInput, cell),
                        CraftingFixture.StationContainer(new ContainerId(id.Value + 1), ContainerKind.StationOutput, cell),
                    ]);
            default:
                var other = TryStation(f, _stationCells[1 - step.Station], out var otherStation) ? otherStation!.Input : new ContainerId(9999);
                return CraftingFixture.StationState(cell, other, output, Recipe);
        }
    }

    /// <summary>10 stone from two chests become 1 gem in the bag; a broken step puts a destroy of items the bag does not hold before the end.</summary>
    private static CompoundOp Craft(ContainerId[] chests, Step step, bool discovered)
    {
        var first = chests[step.Chest];
        var second = chests[(step.Chest + 1) % chests.Length];
        var parts = new List<Operation>
        {
            new ItemDestroyOp(new SlotRef(first, (ushort)step.Slot), CraftingFixture.Stone, 5, ItemSink.Craft),
            new ItemDestroyOp(new SlotRef(second, (ushort)step.Slot), CraftingFixture.Stone, 5, ItemSink.Craft),
            new ItemCreateOp(new SlotRef(Bag, 20), CraftingFixture.Gem, 1, GemLimit, ItemSource.Craft),
        };
        if (!discovered)
        {
            parts.Add(CraftingFixture.Discover(Player1, CraftingFixture.Gem));
        }

        if (step.Broken < 3)
        {
            parts.Insert(step.Broken, new ItemDestroyOp(new SlotRef(Bag, 30), CraftingFixture.Stone, 1, ItemSink.Craft));
        }

        return new CompoundOp(parts);
    }

    /// <summary>The settlement compound of D16: destroy the used input, create the results in the output, set the new state; empty recipe: the state alone.</summary>
    private static CompoundOp Settle(CraftingFixture f, CellPos cell, StationBlockState station, ulong now)
    {
        var inSlots = Slots(f, station.Input);
        var outSlots = Slots(f, station.Output);
        var parts = new List<Operation>();
        var next = station;
        if (station.Recipe.Length > 0)
        {
            var settlement = StationProcess.Settle(inSlots, outSlots, _recipe, Duration, station.Started, new Tick(now), GemLimit);
            TakeFrom(parts, station.Input, inSlots, CraftingFixture.Stone, 2 * settlement.Done, ItemSink.Station);
            TakeFrom(parts, station.Input, inSlots, CraftingFixture.Soil, settlement.Done, ItemSink.Fuel);
            var toPlace = settlement.Done;
            for (var slot = 0; slot < outSlots.Length && toPlace > 0; slot++)
            {
                if (outSlots[slot].IsEmpty || outSlots[slot].Type == CraftingFixture.Gem)
                {
                    var add = Math.Min(GemLimit - outSlots[slot].Count, toPlace);
                    if (add > 0)
                    {
                        parts.Add(new ItemCreateOp(new SlotRef(station.Output, (ushort)slot), CraftingFixture.Gem, (ushort)add, GemLimit, ItemSource.Station));
                        toPlace -= add;
                    }
                }
            }

            Assert.Equal(0, toPlace);
            next = station with { Started = settlement.NextStarted };
        }

        parts.Add(new BlockEntitySetOp(ConstructId, cell, CraftingFixture.StationBlockType, next));
        return new CompoundOp(parts);
    }

    private static void TakeFrom(List<Operation> parts, ContainerId container, ItemStack[] slots, ItemTypeId type, int count, ItemSink sink)
    {
        for (var slot = 0; slot < slots.Length && count > 0; slot++)
        {
            if (slots[slot].Type == type)
            {
                var take = Math.Min((int)slots[slot].Count, count);
                parts.Add(new ItemDestroyOp(new SlotRef(container, (ushort)slot), type, (ushort)take, sink));
                count -= take;
            }
        }

        Assert.Equal(0, count);
    }

    private static ItemStack[] Slots(CraftingFixture f, ContainerId id)
    {
        var container = f.Blocks.Container(id);
        return [.. Enumerable.Range(0, container.Size).Select(container.GetSlot)];
    }

    private static bool TryStation(CraftingFixture f, CellPos cell, out StationBlockState? station)
    {
        station = f.State.BlockEntities.TryGet(new BlockRef(ConstructId, cell), out var state) ? state as StationBlockState : null;
        return station is not null;
    }

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

    private static long[] Totals(CraftingFixture f)
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

    /// <summary>A station state and its two containers agree both ways: kinds, hosts and the block under them.</summary>
    private static void AssertStationsAgree(CraftingFixture f)
    {
        foreach (var container in f.State.Containers.All.Where(c => c.Kind is ContainerKind.StationInput or ContainerKind.StationOutput))
        {
            Assert.Equal(ContainerHostKind.ConstructBlock, container.Host.Kind);
            Assert.True(f.State.BlockEntities.TryGet(new BlockRef(new EntityId(container.Host.Id), container.Host.Cell), out var state), $"container {container.Id.Value} has no station state");
            var station = Assert.IsType<StationBlockState>(state);
            Assert.Equal(container.Id, container.Kind == ContainerKind.StationInput ? station.Input : station.Output);
        }

        foreach (var cell in _stationCells)
        {
            if (TryStation(f, cell, out var station))
            {
                Assert.False(f.Blocks.BlockAt(cell).IsEmpty, $"state of {cell} outlived its block");
                Assert.Equal(ContainerKind.StationInput, f.Blocks.Container(station!.Input).Kind);
                Assert.Equal(ContainerKind.StationOutput, f.Blocks.Container(station.Output).Kind);
                Assert.Equal(ContainerHost.ConstructBlock(ConstructId, cell), f.Blocks.Container(station.Input).Host);
                Assert.Equal(ContainerHost.ConstructBlock(ConstructId, cell), f.Blocks.Container(station.Output).Host);
            }
        }
    }

    private readonly record struct Step(Action Action, int Station, int Chest, int Slot, int Count, int Broken, bool Soil, int Ticks);

    private sealed class Coverage
    {
        public int Placed { get; private set; }

        public int Removed { get; private set; }

        public int Crafted { get; private set; }

        public int PiecesSettled { get; private set; }

        public int SettlementsWithPieces { get; private set; }

        public int Refused { get; private set; }

        public void Refuse() => Refused++;

        public void Accepted(Action action, Operation op)
        {
            switch (action)
            {
                case Action.PlaceStation:
                    Placed++;
                    break;
                case Action.RemoveStation:
                    Removed++;
                    break;
                case Action.CraftFromChests:
                    Crafted++;
                    break;
                case Action.Settle when op is CompoundOp compound:
                    var pieces = compound.Components.Count(c => c is ItemCreateOp);
                    PiecesSettled += pieces;
                    if (pieces > 0)
                    {
                        SettlementsWithPieces++;
                    }

                    break;
            }
        }
    }
}
