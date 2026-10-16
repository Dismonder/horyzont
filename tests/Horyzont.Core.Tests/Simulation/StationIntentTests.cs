using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Crafting.CraftingWorld;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// Stations with time (the furnace; K6-32 D15 to D18) by intent: putting one up and taking it down, choosing the recipe, the settlement before and
/// after an intent that touches a station, and the operation of a settlement. The furnace has 4 input and 2 output slots; the recipe is
/// <c>t:smelt</c> (2 ore and 1 peat to an ingot, 1 s = 60 ticks). Refusals are checked against a valid twin of the intent.
/// </summary>
public sealed class StationIntentTests
{
    private static readonly CellPos _top = new(4, 1, 0);

    private static FunctionalBlockFixture Fixture(int players = 1, GameRules? rules = null, bool withRecipes = true) =>
        ForCrafting(players, withRecipes, false, rules);

    private static PlaceBlockIntent PlaceOnFace(CellPos clicked, Face face, ushort type) => new(new ConstructFaceTarget(ConstructId, clicked, face), type, 0);

    private static PlaceBlockIntent PlaceOnFloor(ushort type) => PlaceOnFace(FunctionalBlocks.FloorCells[4], Face.PosY, type);

    private static PlaceBlockIntent PlaceOnTerrain(FunctionalBlockFixture f, ushort type)
    {
        var eye = f.Sim.Eye(Player1);
        var target = f.Sim.Ground(2f, 6f);
        return new PlaceBlockIntent(new TerrainTarget(SimFixture.Planet, eye, SimFixture.Direction(eye, target), -Vector3.UnitZ), type, 0);
    }

    private static RemoveBlockIntent Remove(CellPos cell) => new(ConstructId, cell);

    private static ItemMoveIntent Move(ContainerId from, int fromSlot, ContainerId to, int toSlot, int count, ItemTypeId type, int expected) =>
        new(new SlotRef(from, (ushort)fromSlot), ItemMoveTarget.ToSlot(new SlotRef(to, (ushort)toSlot)), (ushort)count, type, (ushort)expected);

    [Fact]
    public void PlaceFurnace_CompoundWithTwoContainersAndState()
    {
        var f = Fixture();
        var input = f.State.ContainerIds.PeekNext();
        var output = new ContainerId(input.Value + 1);

        var applied = f.Accept(Player1, PlaceOnFloor(CraftingBlocks.FurnaceType));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        var host = ContainerHost.ConstructBlock(ConstructId, _top);
        Assert.Equal(4, compound.Components.Count);
        Assert.Equal(new ContainerCreateOp(input, ContainerKind.StationInput, host, Player1, ContainerAccess.Shared, 4), compound.Components[0]);
        Assert.Equal(new ContainerCreateOp(output, ContainerKind.StationOutput, host, Player1, ContainerAccess.Shared, 2), compound.Components[1]);
        Assert.Equal(new BlockEntitySetOp(ConstructId, _top, CraftingBlocks.FurnaceType, new StationBlockState(input, output, string.Empty, null)), compound.Components[2]);
        var set = Assert.IsType<BlockSetOp>(compound.Components[3]);
        Assert.Equal((ConstructId, _top, CraftingBlocks.FurnaceType), (set.Construct, set.Cell, set.Block.TypeId));
        Assert.Equal(new StationBlockState(input, output, string.Empty, null), f.StationAt(_top));
    }

    [Fact]
    public void PlaceFurnace_OnTerrain_ConstructCreateLast()
    {
        var f = Fixture();
        var input = f.State.ContainerIds.PeekNext();

        var applied = f.Accept(Player1, PlaceOnTerrain(f, CraftingBlocks.FurnaceType));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(4, compound.Components.Count);
        var create = Assert.IsType<ConstructCreateOp>(compound.Components[3]);
        var host = ContainerHost.ConstructBlock(create.Construct, create.FirstCell);
        Assert.Equal(new ContainerCreateOp(input, ContainerKind.StationInput, host, Player1, ContainerAccess.Shared, 4), compound.Components[0]);
        Assert.Equal(new ContainerCreateOp(new ContainerId(input.Value + 1), ContainerKind.StationOutput, host, Player1, ContainerAccess.Shared, 2), compound.Components[1]);
        Assert.IsType<BlockEntitySetOp>(compound.Components[2]);
    }

    [Fact]
    public void PlaceFurnace_WithoutRecipesContent_InvalidParameters()
    {
        var f = Fixture(withRecipes: false);

        // The twin: a workbench (a station without time) is a plain block and needs no recipes; the furnace has a state and containers and does.
        f.Accept(Player1, PlaceOnFloor(CraftingBlocks.WorkbenchType));
        f.AssertRejected(Player1, PlaceOnFace(_top, Face.PosY, CraftingBlocks.FurnaceType), RejectReason.InvalidParameters);
    }

    [Fact]
    public void PlaceFurnace_AtConstructLimit_InvalidParameters()
    {
        // The twin (review B1, 7): with the default rules the same two furnaces are accepted, so the refusal below comes from the limit and not from the cell.
        var twin = Fixture();
        twin.Accept(Player1, PlaceOnFloor(CraftingBlocks.FurnaceType));
        twin.Accept(Player1, PlaceOnFace(_top, Face.PosX, CraftingBlocks.FurnaceType));

        var f = Fixture(rules: new GameRules { MaxTimedStationsPerConstruct = 1 });
        f.Accept(Player1, PlaceOnFloor(CraftingBlocks.FurnaceType));

        f.AssertRejected(Player1, PlaceOnFace(_top, Face.PosX, CraftingBlocks.FurnaceType), RejectReason.InvalidParameters);

        // The limit counts the stations that stand, not the ones put up: taking the first down makes room.
        f.Accept(Player1, Remove(_top));
        f.Accept(Player1, PlaceOnFloor(CraftingBlocks.FurnaceType));
    }

    [Fact]
    public void PlaceWorkbench_PlainBlock()
    {
        var f = Fixture();

        var applied = f.Accept(Player1, PlaceOnFloor(CraftingBlocks.WorkbenchType));

        var set = Assert.IsType<BlockSetOp>(applied.Operation);
        Assert.Equal(CraftingBlocks.WorkbenchType, set.Block.TypeId);
        Assert.False(f.HasState(_top, out _));
        Assert.Empty(f.HostedBy(_top));
    }

    [Fact]
    public void SelectRecipe_WithInput_StartedNow()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell);
        f.Stock(input, 0, Ore, 4);
        f.Stock(input, 1, Peat, 2);
        f.Discover(Player1, Ore);
        f.Wait(5);
        var journaled = f.JournalCount();

        var applied = f.Accept(Player1, Craft("t:smelt", 1, FurnaceCell));

        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", f.State.CurrentTick)),
            applied.Operation);
        Assert.Equal(journaled + 1, f.JournalCount());
    }

    [Fact]
    public void SelectRecipe_EmptyInput_Idle()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell);
        f.Discover(Player1, Ore);

        var applied = f.Accept(Player1, Craft("t:smelt", 1, FurnaceCell));

        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", null)),
            applied.Operation);
    }

    [Fact]
    public void SelectSameRecipe_InvalidParameters()
    {
        var f = Fixture();
        f.SetUpTimedStation(FurnaceCell);
        f.Discover(Player1, Ore);
        f.Accept(Player1, Craft("t:smelt", 1, FurnaceCell));

        f.AssertRejected(Player1, Craft("t:smelt", 1, FurnaceCell), RejectReason.InvalidParameters);
    }

    [Fact]
    public void SelectRecipe_Count2_InvalidParameters()
    {
        var f = Fixture();
        f.SetUpTimedStation(FurnaceCell);
        f.Discover(Player1, Ore);

        f.AssertRejected(Player1, Craft("t:smelt", 2, FurnaceCell), RejectReason.InvalidParameters);
        f.Accept(Player1, Craft("t:smelt", 1, FurnaceCell));
    }

    [Fact]
    public void MoveOreIntoIdleFurnace_PostSettleStarts()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt");
        f.Stock(Bag, 0, Ore, 4);
        f.Stock(Bag, 1, Peat, 2);
        f.Wait(30);
        var journaled = f.JournalCount();

        // The ore alone starts nothing: no fuel, so the settlement after the move changes nothing and writes no operation.
        f.Accept(Player1, Move(Bag, 0, input, 0, 4, Ore, 4));
        Assert.Single(f.JournalFrom(journaled));

        f.Accept(Player1, Move(Bag, 1, input, 1, 2, Peat, 2));

        var ops = f.JournalFrom(journaled + 1);
        Assert.Equal(2, ops.Count);
        Assert.IsType<ItemTransferOp>(ops[0]);
        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", f.State.CurrentTick)),
            ops[1]);
    }

    [Fact]
    public void MoveIntoOutput_InvalidParameters()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell);
        f.Stock(Bag, 0, Ore, 4);
        f.Accept(Player1, Move(Bag, 0, input, 0, 2, Ore, 4));

        f.AssertRejected(Player1, Move(Bag, 0, output, 0, 2, Ore, 2), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, new ItemMoveIntent(new SlotRef(Bag, 0), ItemMoveTarget.AnyIn(output), 2, Ore, 2), RejectReason.InvalidParameters);
    }

    [Fact]
    public void TakeAllFromOutput_PreSettleIncludesFinished()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt", f.State.CurrentTick);
        f.Stock(input, 0, Ore, 4);
        f.Stock(input, 1, Peat, 2);
        f.Wait(120);
        var journaled = f.JournalCount();

        f.Accept(Player1, new TakeAllIntent(output));

        // Two pieces were finished at tick 120 but only the settlement before the intent put them into the output.
        var ops = f.JournalFrom(journaled);
        Assert.IsType<CompoundOp>(ops[0]);
        Assert.Equal(2, f.CountIn(Bag, Ingot));
        Assert.Equal(0, f.CountIn(output, Ingot));
        Assert.Equal(0, f.CountIn(input, Ore));
    }

    [Fact]
    public void RemoveFurnace_NotEmpty_ContainerNotEmpty()
    {
        var f = Fixture();
        var (input, _) = f.SetUpTimedStation(FurnaceCell);
        f.Stock(input, 0, Ore, 1);

        f.AssertRejected(Player1, Remove(FurnaceCell), RejectReason.ContainerNotEmpty);
        Assert.False(f.BlockAt(FurnaceCell).IsEmpty);

        // The twin (review B1, 16): once the ore is gone the same removal is accepted.
        f.Apply(new ItemDestroyOp(new SlotRef(input, 0), Ore, 1, ItemSink.Admin));
        f.Accept(Player1, Remove(FurnaceCell));
        Assert.True(f.BlockAt(FurnaceCell).IsEmpty);
    }

    [Fact]
    public void RemoveFurnace_Empty_Compound()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell);

        var applied = f.Accept(Player1, Remove(FurnaceCell));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ContainerRemoveOp(input),
                new ContainerRemoveOp(output),
                new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, null),
                new BlockClearOp(ConstructId, FurnaceCell),
            ],
            compound.Components);
        Assert.False(f.State.Containers.TryGet(input, out _));
        Assert.False(f.HasState(FurnaceCell, out _));
    }

    [Fact]
    public void RemoveFurnace_FinishedItemsInOutput_ContainerNotEmpty()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt", f.State.CurrentTick);
        f.Stock(input, 0, Ore, 2);
        f.Stock(input, 1, Peat, 1);
        f.Wait(60);
        var journaled = f.JournalCount();

        // The settlement before the removal moves the finished piece into the output; it stays applied although the intent is refused.
        Assert.Equal(RejectReason.ContainerNotEmpty, f.Submit(Remove(FurnaceCell)).Reason);

        Assert.IsType<CompoundOp>(Assert.Single(f.JournalFrom(journaled)));
        Assert.Equal(1, f.CountIn(output, Ingot));
        Assert.Equal(0, f.CountIn(input, Ore));
        Assert.False(f.BlockAt(FurnaceCell).IsEmpty);
    }

    [Fact]
    public void ConfigureAccessOnFurnace_InvalidParameters()
    {
        var f = Fixture();
        f.SetUpTimedStation(FurnaceCell);
        f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        var access = new ContainerAccessConfig(ContainerAccess.Private);

        // The twin: the same setting on a chest is accepted. A station keeps its containers shared (D17).
        f.Accept(Player1, new BlockConfigIntent(ConstructId, FunctionalBlocks.ChestCell, access));
        f.AssertRejected(Player1, new BlockConfigIntent(ConstructId, FurnaceCell, access), RejectReason.InvalidParameters);
    }

    [Fact]
    public void QuickStack_IgnoresStationContainers()
    {
        var f = Fixture();
        var (input, _) = f.SetUpTimedStation(FurnaceCell);
        // Review B1, 5: the chest has room for only 2 more ore (498 of 500), so 3 stay in the bag; a quick stack that treated the input of the furnace as a
        // chest would put them there.
        var chest = f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Ore, 498), (Soil, 500), (Soil, 500), (Soil, 500));
        f.Stock(input, 0, Ore, 1);
        f.Stock(Bag, 0, Ore, 5);

        f.Accept(Player1, new QuickStackIntent());

        Assert.Equal(500, f.CountIn(chest, Ore));
        Assert.Equal(1, f.CountIn(input, Ore));
        Assert.Equal(3, f.CountIn(Bag, Ore));
    }

    [Fact]
    public void MoveBetweenTwoFurnaces_SettlesBoth_InBlockRefOrder()
    {
        // Review B1, 10: the settlements before and after an intent go in ascending BlockRef order, not in the order the intent names the stations.
        // A (FurnaceCell) runs and has a piece due; B (FurnaceCell2, the later ref and the source of the move) has a full output, so its piece is stalled.
        var f = Fixture();
        var started = f.State.CurrentTick;
        var (inA, outA) = f.SetUpTimedStation(FurnaceCell, "t:smelt", started);
        var (inB, outB) = f.SetUpTimedStation(FurnaceCell2, "t:smelt", started);
        f.Stock(inA, 0, Ore, 4);
        f.Stock(inA, 1, Peat, 1);
        f.Stock(inB, 0, Ore, 4);
        f.Stock(inB, 1, Peat, 2);
        f.Stock(outB, 0, Ingot, 5);
        f.Stock(outB, 1, Peat, 1);
        f.Wait(60);
        var journaled = f.JournalCount();

        // The peat leaves the output of B: A gets the fuel for its second piece, B gets room for an ingot.
        f.Accept(Player1, Move(outB, 1, inA, 1, 1, Peat, 1));

        var ops = f.JournalFrom(journaled);
        Assert.Equal(5, ops.Count);

        // Before: A settles its piece (a compound), B only loses its start (nothing fits); the intent; after: A starts the second piece, B starts again.
        var settleA = Assert.IsType<CompoundOp>(ops[0]);
        Assert.IsType<BlockEntitySetOp>(settleA.Components[^1]);
        Assert.Equal(FurnaceCell, ((BlockEntitySetOp)settleA.Components[^1]).Cell);
        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell2, CraftingBlocks.FurnaceType, new StationBlockState(inB, outB, "t:smelt", null)),
            ops[1]);
        Assert.IsType<ItemTransferOp>(ops[2]);
        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(inA, outA, "t:smelt", f.State.CurrentTick)),
            ops[3]);
        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell2, CraftingBlocks.FurnaceType, new StationBlockState(inB, outB, "t:smelt", f.State.CurrentTick)),
            ops[4]);
    }

    [Fact]
    public void TrySettleStation_TwoPieces_ExactOperation()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt", f.State.CurrentTick);
        f.Stock(input, 0, Ore, 4);
        f.Stock(input, 1, Peat, 2);
        f.Wait(120);

        var applied = f.Sim.Sim.TrySettleStation(At(FurnaceCell));

        var compound = Assert.IsType<CompoundOp>(Assert.IsType<AppliedOperation>(applied).Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(input, 0), Ore, 4, ItemSink.Station),
                new ItemDestroyOp(new SlotRef(input, 1), Peat, 2, ItemSink.Fuel),
                new ItemCreateOp(new SlotRef(output, 0), Ingot, 2, 5, ItemSource.Station),
                new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", null)),
            ],
            compound.Components);
    }

    [Fact]
    public void TrySettleStation_FuelInLowerSlot_IngredientsFirst()
    {
        // Review B1, 11: the ingredients are taken before the fuel, whatever the slots: the peat lies in slot 0, the ore in slot 1.
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt", f.State.CurrentTick);
        f.Stock(input, 0, Peat, 1);
        f.Stock(input, 1, Ore, 2);
        f.Wait(60);

        var applied = f.Sim.Sim.TrySettleStation(At(FurnaceCell));

        var compound = Assert.IsType<CompoundOp>(Assert.IsType<AppliedOperation>(applied).Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(input, 1), Ore, 2, ItemSink.Station),
                new ItemDestroyOp(new SlotRef(input, 0), Peat, 1, ItemSink.Fuel),
                new ItemCreateOp(new SlotRef(output, 0), Ingot, 1, 5, ItemSource.Station),
                new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", null)),
            ],
            compound.Components);
    }

    [Fact]
    public void TrySettleStation_ThreePossibleTwoDone_StartAdvancedByDoneDurations()
    {
        var f = Fixture();
        var started = f.State.CurrentTick;
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt", started);
        f.Stock(input, 0, Ore, 6);
        f.Stock(input, 1, Peat, 3);
        f.Wait(130);

        var applied = f.Sim.Sim.TrySettleStation(At(FurnaceCell));

        var compound = Assert.IsType<CompoundOp>(Assert.IsType<AppliedOperation>(applied).Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(input, 0), Ore, 4, ItemSink.Station),
                new ItemDestroyOp(new SlotRef(input, 1), Peat, 2, ItemSink.Fuel),
                new ItemCreateOp(new SlotRef(output, 0), Ingot, 2, 5, ItemSource.Station),
                new BlockEntitySetOp(
                    ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", new Tick(started.Value + 120))),
            ],
            compound.Components);
    }

    [Fact]
    public void TrySettleStation_IdleWithInput_OnlyTheStartChanges()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt");
        f.Stock(input, 0, Ore, 2);
        f.Stock(input, 1, Peat, 1);
        f.Wait(10);

        var applied = f.Sim.Sim.TrySettleStation(At(FurnaceCell));

        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", f.State.CurrentTick)),
            Assert.IsType<AppliedOperation>(applied).Operation);
    }

    [Fact]
    public void TrySettleStation_NothingChanges_ReturnsNullAndWritesNothing()
    {
        var f = Fixture();
        var (input, _) = f.SetUpTimedStation(FurnaceCell, "t:smelt", f.State.CurrentTick);
        f.Stock(input, 0, Ore, 2);
        f.Stock(input, 1, Peat, 1);
        f.SetUpTimedStation(FurnaceCell2);
        f.EnsureBlock(FunctionalBlocks.ChestCell, FunctionalBlocks.ChestType);
        f.Wait(30);
        var journaled = f.JournalCount();

        // Less than one duration has passed; a station without a recipe and a block without a station state have nothing to settle.
        Assert.Null(f.Sim.Sim.TrySettleStation(At(FurnaceCell)));
        Assert.Null(f.Sim.Sim.TrySettleStation(At(FurnaceCell2)));
        Assert.Null(f.Sim.Sim.TrySettleStation(At(FunctionalBlocks.ChestCell)));
        Assert.Equal(journaled, f.JournalCount());
    }

    [Fact]
    public void Submit_JournalOrder_SettleBefore_Intent_Discovery_SettleAfter()
    {
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt", f.State.CurrentTick);
        f.Stock(input, 0, Ore, 4);
        f.Stock(input, 1, Peat, 2);

        // The output has room for one more ingot (5 and 4 of a stack of 5): two pieces are due, one fits. The player has not discovered the ingot.
        f.Stock(output, 0, Ingot, 5);
        f.Stock(output, 1, Ingot, 4);
        f.Wait(120);
        var journaled = f.JournalCount();
        var applied = new List<Operation>();
        f.Sim.Sim.Applied += a => applied.Add(a.Operation);

        f.Accept(Player1, new TakeAllIntent(output));

        // One entry per step, in this order: the settlement before the intent, the intent, the discovery of the ingot it brought, the settlement after
        // it (the emptied output lets the second piece start now).
        var ops = f.JournalFrom(journaled);
        Assert.Equal(4, ops.Count);
        var before = Assert.IsType<CompoundOp>(ops[0]);
        Assert.Contains(new ItemCreateOp(new SlotRef(output, 1), Ingot, 1, 5, ItemSource.Station), before.Components);
        var take = Assert.IsType<CompoundOp>(ops[1]);
        Assert.All(take.Components, c => Assert.IsType<ItemTransferOp>(c));
        Assert.Equal(new ItemDiscoverOp(Player1, Ingot), ops[2]);
        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", f.State.CurrentTick)),
            ops[3]);
        Assert.Equal(ops, applied);
    }

    [Fact]
    public void SelectOtherRecipe_WhileRunning_SettlesOldThenStartsNew()
    {
        // Review A+B, fix 6: a furnace smelting (one piece done, the next one under way) is switched to another recipe. The old recipe is settled first
        // (one piece), then the new recipe starts; the progress of the piece under way is lost and its ingredients stay in the input (D15).
        var f = Fixture();
        var started = f.State.CurrentTick;
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt", started);
        f.Stock(input, 0, Ore, 4);
        f.Stock(input, 1, Peat, 2);
        f.Stock(input, 2, Soil, 2);
        f.Discover(Player1, Ore, Soil);
        f.Wait(90);
        var journaled = f.JournalCount();

        f.Accept(Player1, Craft("t:smelt2", 1, FurnaceCell));

        var ops = f.JournalFrom(journaled);
        Assert.Equal(2, ops.Count);
        var settle = Assert.IsType<CompoundOp>(ops[0]);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(input, 0), Ore, 2, ItemSink.Station),
                new ItemDestroyOp(new SlotRef(input, 1), Peat, 1, ItemSink.Fuel),
                new ItemCreateOp(new SlotRef(output, 0), Ingot, 1, 5, ItemSource.Station),
                new BlockEntitySetOp(
                    ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", new Tick(started.Value + 60))),
            ],
            settle.Components);
        Assert.Equal(
            new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt2", f.State.CurrentTick)),
            ops[1]);
        Assert.Equal(2, f.CountIn(input, Ore));
        Assert.Equal(1, f.CountIn(input, Peat));
        Assert.Equal(1, f.CountIn(output, Ingot));
    }

    [Fact]
    public void TrySettleStation_OutputFull_DoneLimitedThenIdle()
    {
        // Review A+B, fix 6: two pieces are due, the output has room for one. The compound ends with the state of an idle station (Started = null).
        var f = Fixture();
        var (input, output) = f.SetUpTimedStation(FurnaceCell, "t:smelt", f.State.CurrentTick);
        f.Stock(input, 0, Ore, 4);
        f.Stock(input, 1, Peat, 2);
        f.Stock(output, 0, Ingot, 5);
        f.Stock(output, 1, Ingot, 4);
        f.Wait(120);

        var applied = f.Sim.Sim.TrySettleStation(At(FurnaceCell));

        var compound = Assert.IsType<CompoundOp>(Assert.IsType<AppliedOperation>(applied).Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(input, 0), Ore, 2, ItemSink.Station),
                new ItemDestroyOp(new SlotRef(input, 1), Peat, 1, ItemSink.Fuel),
                new ItemCreateOp(new SlotRef(output, 1), Ingot, 1, 5, ItemSource.Station),
                new BlockEntitySetOp(ConstructId, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", null)),
            ],
            compound.Components);
        Assert.Null(f.StationAt(FurnaceCell).Started);
    }

    [Fact]
    public void TrySettleStation_NothingChanges_AllocatesNothing()
    {
        // Review A+B, fix 5: the idle observation of a station (every 10 ticks per observed station) translates no recipe and copies no slots.
        var f = Fixture();
        var (input, _) = f.SetUpTimedStation(FurnaceCell, "t:smelt", f.State.CurrentTick);
        f.Stock(input, 0, Ore, 2);
        f.Stock(input, 1, Peat, 1);
        f.Wait(10);
        var station = At(FurnaceCell);
        Assert.Null(f.Sim.Sim.TrySettleStation(station));
        for (var i = 0; i < 100; i++)
        {
            f.Sim.Sim.TrySettleStation(station);
        }

        var allocated = long.MaxValue;
        for (var round = 0; round < 60 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                f.Sim.Sim.TrySettleStation(station);
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(0, allocated);
    }
}
