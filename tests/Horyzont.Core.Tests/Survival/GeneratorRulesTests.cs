using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Crafting;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Survival.HazardWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-34 D12: placing and removing an oxygen generator (<c>GeneratorRules</c>), settling it with time (<c>GeneratorSettlements</c>, through <see cref="Horyzont.Core.Simulation.ServerSimulation.TrySettleStation"/>)
/// and the rule that every path that changes the input settles first (review B1, P2-2). One fuel pack burns <see cref="HazardTestContent.TicksPerCell"/> ticks (600). A generator placed by an intent stands
/// in <see cref="PlacedCell"/> with a two-slot input. Every refusal first accepts a valid twin.
/// </summary>
public sealed class GeneratorRulesTests
{
    private const ulong Per = HazardTestContent.TicksPerCell;

    private static BlockRef At(CellPos cell) => new(FunctionalBlockFixture.ConstructId, cell);

    private static BlockEntitySetOp StateOp(CellPos cell, ContainerId input, Tick? started) =>
        new(FunctionalBlockFixture.ConstructId, cell, HazardBlocks.GeneratorType, new OxygenGeneratorBlockState(input, started));

    private static SlotRef Slot(ContainerId container, int slot) => new(container, (ushort)slot);

    /// <summary>A world with a generator placed by an intent and, optionally, a started state and fuel (server operations: they do not settle).</summary>
    private static (HazardWorld World, ContainerId Input) Generator(Tick? started = null, int slot0 = 0, int slot1 = 0)
    {
        var w = new HazardWorld(survival: true);
        var input = w.PlaceByIntent(PlacedCell);
        if (started is { } start)
        {
            w.Apply(StateOp(PlacedCell, input, start));
        }

        if (slot0 > 0)
        {
            w.FeedGenerator(input, 0, slot0);
        }

        if (slot1 > 0)
        {
            w.FeedGenerator(input, 1, slot1);
        }

        return (w, input);
    }

    private static int FuelIn(HazardWorld w, ContainerId container)
    {
        var total = 0;
        var c = w.Blocks.Container(container);
        for (var i = 0; i < c.Size; i++)
        {
            total += c.GetSlot(i).Type == HazardWorld.Fuel ? c.GetSlot(i).Count : 0;
        }

        return total;
    }

    [Fact]
    public void Place_CreatesCompound()
    {
        var w = new HazardWorld(survival: true);
        var input = w.State.ContainerIds.PeekNext();

        var applied = w.Blocks.Accept(Player1, PlaceGeneratorIntent());

        Assert.Equal(
            [
                new ContainerCreateOp(input, ContainerKind.StationInput, ContainerHost.ConstructBlock(FunctionalBlockFixture.ConstructId, PlacedCell), Player1, ContainerAccess.Shared, 2),
                StateOp(PlacedCell, input, null),
                FunctionalBlockFixture.PlaceBlock(PlacedCell, HazardBlocks.GeneratorType),
            ],
            Assert.IsType<CompoundOp>(applied.Operation).Components);
        Assert.Equal(new OxygenGeneratorBlockState(input, null), w.GeneratorAt(PlacedCell));
    }

    [Fact]
    public void Place_WithoutHazardContent_InvalidParameters()
    {
        var with = new HazardWorld(survival: true);
        with.Blocks.Accept(Player1, PlaceGeneratorIntent());

        var without = new HazardWorld(survival: true, hazardContent: false);
        without.Blocks.AssertRejected(Player1, PlaceGeneratorIntent(), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Place_ConstructFull_InvalidParameters()
    {
        var w = new HazardWorld(survival: true);
        for (var i = 0; i < ProtocolConstants.MaxBlockEntitiesPerConstruct - 1; i++)
        {
            w.Apply(w.PlaceGenerator(CraftingFixture.GridCell(i)));
        }

        // The last free entity of the construct takes the first generator; the second finds the construct full (MaxBlockEntitiesPerConstruct).
        w.Blocks.Accept(Player1, PlaceGeneratorIntent());
        w.Blocks.AssertRejected(Player1, PlaceSecondGeneratorIntent(), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Remove_NonEmpty_ContainerNotEmpty()
    {
        // A stone in the input of an idle generator and a queued cell of a generator that started just now: neither is settled into anything, so the refusal changes nothing.
        var (idle, idleInput) = Generator();
        idle.Apply(new ItemCreateOp(Slot(idleInput, 0), Stone, 1, 100, ItemSource.Admin));
        idle.Blocks.AssertRejected(Player1, RemoveGeneratorIntent(PlacedCell), RejectReason.ContainerNotEmpty);

        var (running, input) = Generator(started: null);
        running.Apply(StateOp(PlacedCell, input, running.Now));
        running.FeedGenerator(input, 0, 1);
        running.Blocks.AssertRejected(Player1, RemoveGeneratorIntent(PlacedCell), RejectReason.ContainerNotEmpty);
        Assert.NotNull(running.GeneratorAt(PlacedCell));
    }

    [Fact]
    public void Remove_Empty_Compound()
    {
        var (w, input) = Generator();

        var applied = w.Blocks.Accept(Player1, RemoveGeneratorIntent(PlacedCell));

        Assert.Equal(
            [new ContainerRemoveOp(input), FunctionalBlockFixture.ClearState(PlacedCell, HazardBlocks.GeneratorType), new BlockClearOp(FunctionalBlockFixture.ConstructId, PlacedCell)],
            Assert.IsType<CompoundOp>(applied.Operation).Components);
        Assert.Null(w.GeneratorAt(PlacedCell));
    }

    [Fact]
    public void MoveIntoInput_SettlesAfter_StartsNow()
    {
        var (w, input) = Generator();
        w.Give(Player1, HazardWorld.Fuel, 3);
        w.AdvanceTicks(100);
        var now = w.Now;

        w.Blocks.Accept(Player1, new ItemMoveIntent(BagSlot(Player1, 0), ItemMoveTarget.ToSlot(Slot(input, 0)), 3, HazardWorld.Fuel, 3));

        // The idle generator takes the cells, settles after the move: one cell is destroyed at once (the burning one) and the start is the tick of the move.
        Assert.Equal(new OxygenGeneratorBlockState(input, now), w.GeneratorAt(PlacedCell));
        Assert.Equal(2, FuelIn(w, input));
    }

    [Fact]
    public void TakeOutQueuedCells_AfterPeriods_BurnedOnesDestroyedFirst()
    {
        var (w, input) = Generator();
        w.Give(Player1, HazardWorld.Fuel, 4);
        w.Blocks.Accept(Player1, new ItemMoveIntent(BagSlot(Player1, 0), ItemMoveTarget.ToSlot(Slot(input, 0)), 4, HazardWorld.Fuel, 4));
        var started = w.Now;
        Assert.Equal(3, FuelIn(w, input));

        // Two and a half periods later two of the three queued cells have burned: taking everything out gives back one cell, not three.
        w.AdvanceTicks((int)((2 * Per) + (Per / 2)));
        w.Blocks.Accept(Player1, new TakeAllIntent(input));

        Assert.Equal(1, FuelIn(w, BagOf(Player1)));
        Assert.Equal(0, FuelIn(w, input));
        Assert.Equal(new OxygenGeneratorBlockState(input, new Tick(started.Value + (2 * Per))), w.GeneratorAt(PlacedCell));
    }

    [Theory]
    [InlineData("MoveToSlotIn")]
    [InlineData("MoveAnyIn")]
    [InlineData("MoveToSlotOut")]
    [InlineData("MoveAnyOut")]
    [InlineData("TakeAllOut")]
    [InlineData("RemoveBlock")]
    public void EveryInputMutationPath_SettlesBefore(string path)
    {
        var intoInput = path.EndsWith("In", StringComparison.Ordinal);

        // In: a generator that started now with an empty queue, three periods and a tick past its only cell, so it is idle in truth and "running" only by its stale state.
        // Out: a generator with four queued cells, two periods and a tick behind it, so two of them have burned and two are left.
        // RemoveBlock: one queued cell, two periods and a tick behind it, so the cell has burned in truth while the view of the client still shows it (review B1b, P3-3).
        var (w, input) = intoInput ? Generator() : Generator(slot0: path == "RemoveBlock" ? 1 : 4);
        var started = w.Now;
        w.Apply(StateOp(PlacedCell, input, started));
        if (intoInput)
        {
            w.Give(Player1, HazardWorld.Fuel, 2);
        }

        w.AdvanceTicks(intoInput ? (int)((3 * Per) + 1) : (int)((2 * Per) + 1));
        var now = w.Now;
        var bag = BagSlot(Player1, 0);
        Intent intent = path switch
        {
            "MoveToSlotIn" => new ItemMoveIntent(bag, ItemMoveTarget.ToSlot(Slot(input, 0)), 2, HazardWorld.Fuel, 2),
            "MoveAnyIn" => new ItemMoveIntent(bag, ItemMoveTarget.AnyIn(input), 2, HazardWorld.Fuel, 2),
            "MoveToSlotOut" => new ItemMoveIntent(Slot(input, 0), ItemMoveTarget.ToSlot(bag), 2, HazardWorld.Fuel, 2),
            "MoveAnyOut" => new ItemMoveIntent(Slot(input, 0), ItemMoveTarget.AnyIn(BagOf(Player1)), 2, HazardWorld.Fuel, 2),
            "RemoveBlock" => RemoveGeneratorIntent(PlacedCell),
            _ => new TakeAllIntent(input),
        };

        // The expected count of the move out is the settled one (2, not the 4 of the stale view): the settlement runs before the intent is checked.
        w.Blocks.Accept(Player1, intent);

        if (path == "RemoveBlock")
        {
            // The only cell burned: the settlement before empties the input, so the removal is accepted (without it the stale queue would be ContainerNotEmpty).
            Assert.Null(w.GeneratorAt(PlacedCell));
            Assert.Equal(0, FuelIn(w, BagOf(Player1)));
        }
        else if (intoInput)
        {
            // Settled before: the generator was idle, so the two new cells are not "burned in the past": one starts now, one waits.
            Assert.Equal(1, FuelIn(w, input));
            Assert.Equal(new OxygenGeneratorBlockState(input, now), w.GeneratorAt(PlacedCell));
        }
        else
        {
            // Two of the four cells burned: two come back, not four (without the settlement before they would be free protection).
            Assert.Equal(2, FuelIn(w, BagOf(Player1)));
            Assert.Equal(0, FuelIn(w, input));
            Assert.Equal(new OxygenGeneratorBlockState(input, new Tick(started.Value + (2 * Per))), w.GeneratorAt(PlacedCell));
        }
    }

    [Fact]
    public void StaleExpectation_ContainerChanged_SettlementStays()
    {
        var (w, input) = Generator(slot0: 4);
        var started = w.Now;
        w.Apply(StateOp(PlacedCell, input, started));
        w.AdvanceTicks((int)(2 * Per) + 1);
        var entries = w.Blocks.Sim.Journal.Entries.Count;

        // The client still sees 4 queued cells; the settlement before the intent leaves 2, so the expected count of the move no longer holds (D16a): the move is refused,
        // the settlement stays applied (ServerSimulation.Submit) and is the only new entry of the journal (review B1b, P3-3).
        w.Blocks.AssertRejected(
            Player1, new ItemMoveIntent(Slot(input, 0), ItemMoveTarget.ToSlot(BagSlot(Player1, 0)), 2, HazardWorld.Fuel, 4), RejectReason.ContainerChanged);

        Assert.Equal(2, FuelIn(w, input));
        Assert.Equal(0, FuelIn(w, BagOf(Player1)));
        Assert.Equal(new OxygenGeneratorBlockState(input, new Tick(started.Value + (2 * Per))), w.GeneratorAt(PlacedCell));
        Assert.Equal(entries + 1, w.Blocks.Sim.Journal.Entries.Count);
    }

    [Fact]
    public void TrySettle_BurnedCells_DestroyFuelThenState()
    {
        var (w, input) = Generator(slot0: 1, slot1: 2);
        var started = w.Now;
        w.Apply(StateOp(PlacedCell, input, started));
        w.AdvanceTicks((int)(2 * Per) + 5);

        var applied = w.Simulation.TrySettleStation(At(PlacedCell));

        // Two periods are behind the generator: the cells burned are taken from the slots in ascending order, then the new start.
        Assert.Equal(
            [
                new ItemDestroyOp(Slot(input, 0), HazardWorld.Fuel, 1, ItemSink.Fuel),
                new ItemDestroyOp(Slot(input, 1), HazardWorld.Fuel, 1, ItemSink.Fuel),
                StateOp(PlacedCell, input, new Tick(started.Value + (2 * Per))),
            ],
            Assert.IsType<CompoundOp>(Assert.IsType<AppliedOperation>(applied).Operation).Components);
    }

    [Fact]
    public void TrySettle_NothingChanged_NoOperation()
    {
        var (w, input) = Generator(slot0: 2);
        w.Apply(StateOp(PlacedCell, input, w.Now));
        w.AdvanceTicks((int)Per - 1);

        // One tick before the first period: no cell burned, the start is the same.
        Assert.Null(w.Simulation.TrySettleStation(At(PlacedCell)));

        w.AdvanceTicks(1);
        Assert.NotNull(w.Simulation.TrySettleStation(At(PlacedCell)));
    }

    [Fact]
    public void Settlement_IndependentOfFrequency_SameItemsAndState()
    {
        static (int Fuel, OxygenGeneratorBlockState? State) Run(int step)
        {
            var (w, input) = Generator(slot0: 3, slot1: 2);
            w.Apply(StateOp(PlacedCell, input, w.Now));
            const int total = 2500;
            for (var elapsed = 0; elapsed < total; elapsed += step)
            {
                w.AdvanceTicks(Math.Min(step, total - elapsed));
                w.Simulation.TrySettleStation(At(PlacedCell));
            }

            return (FuelIn(w, input), w.GeneratorAt(PlacedCell));
        }

        // The same 2500 ticks settled in one go and every 7, 61 and 600 ticks end with the same items and the same state (the settlement is a function of ticks).
        var once = Run(2500);
        Assert.Equal(1, once.Fuel);
        foreach (var step in new[] { 7, 61, 600 })
        {
            var stepped = Run(step);
            Assert.Equal(once.Fuel, stepped.Fuel);
            Assert.Equal(once.State!.Started, stepped.State!.Started);
        }
    }

    [Fact]
    public void Input_NonFuel_NotCountedAndBlocksRemoval()
    {
        var (w, input) = Generator();
        w.Apply(new ItemCreateOp(Slot(input, 0), Stone, 4, 100, ItemSource.Admin));
        w.AdvanceTicks((int)Per);

        // The input takes anything (D12), but only fuel counts: an idle generator with stones stays idle and the stones keep the generator from being taken down.
        Assert.Null(w.Simulation.TrySettleStation(At(PlacedCell)));
        Assert.Equal(new OxygenGeneratorBlockState(input, null), w.GeneratorAt(PlacedCell));
        w.Blocks.AssertRejected(Player1, RemoveGeneratorIntent(PlacedCell), RejectReason.ContainerNotEmpty);
    }
}
