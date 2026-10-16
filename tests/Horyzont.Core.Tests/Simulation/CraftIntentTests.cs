using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Crafting;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Crafting.CraftingWorld;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// <see cref="CraftIntent"/> (intent 24, K6-32 D8, D12 to D14) for the hands and the stations without time: sources of the ingredients, the level of a
/// workbench, the reach of a station, the visibility of a recipe, the room for the result. The simulation has the test recipes of
/// <see cref="SurvivalContentFactory.RecipesJson"/>. Every test that expects a refusal first accepts a valid twin of the intent (or accepts it right
/// after), because an unknown intent is refused today and the refusal alone would prove nothing. The reject reasons 44 to 46 are numbers until step B2.
/// </summary>
public sealed class CraftIntentTests
{
    [Fact]
    public void Craft_CheapRefusalAfterOtherPlayersQuickStack_StaysFree()
    {
        var f = Fixture(players: 2);
        f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Gem, 1));
        f.Stock(Bag, 0, Stone, 10);
        f.AssertRejected(Player1, new QuickStackIntent(), RejectReason.NotEnoughItems);

        for (var i = 0; i < f.Sim.Sim.Rules.ItemIntentBurst + 10; i++)
        {
            f.AssertRejected(Player2, Craft("t:nope"), RejectReason.InvalidParameters);
        }
    }

    private const RejectReason StationRequired = (RejectReason)44;
    private const RejectReason RecipeLocked = (RejectReason)45;
    private const RejectReason TooFarFromStation = (RejectReason)46;

    private static FunctionalBlockFixture Fixture(int players = 1) => ForCrafting(players);

    /// <summary>A fixture with a workbench in <see cref="BenchCell"/> and the player knowing the brick, the ingot and the stone.</summary>
    private static FunctionalBlockFixture WithBench(int players = 1)
    {
        var f = Fixture(players);
        f.EnsureBlock(BenchCell, CraftingBlocks.WorkbenchType);
        f.Discover(Player1, Brick, Ingot, Stone);
        return f;
    }

    [Fact]
    public void Hands_FromBag_CompoundDestroyCreateDiscover()
    {
        var f = Fixture();
        f.Stock(Bag, 0, Stone, 4);
        f.Discover(Player1, Stone);
        var journaled = f.JournalCount();

        var applied = f.Accept(Player1, Craft("t:hand"));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(Bag, 0), Stone, 2, ItemSink.Craft),
                new ItemCreateOp(new SlotRef(Bag, 1), Brick, 1, 500, ItemSource.Craft),
                new ItemDiscoverOp(Player1, Brick),
            ],
            compound.Components);
        Assert.Equal(new ItemStack(Stone, 2), f.Container(Bag).GetSlot(0));
        Assert.Equal(new ItemStack(Brick, 1), f.Container(Bag).GetSlot(1));
        Assert.True(f.IsDiscovered(Player1, Brick));
        Assert.Equal(journaled + 1, f.JournalCount());
    }

    [Fact]
    public void Hands_WithStation_InvalidParameters()
    {
        var f = Fixture();
        f.EnsureBlock(BenchCell, CraftingBlocks.WorkbenchType);
        f.Stock(Bag, 0, Stone, 8);
        f.Discover(Player1, Stone);
        f.Accept(Player1, Craft("t:hand"));

        f.AssertRejected(Player1, Craft("t:hand", 1, BenchCell), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Hands_ChestWithin20mOfPlayer_Used()
    {
        var f = Fixture();
        var chest = f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Stone, 1));
        f.Stock(Bag, 0, Stone, 1);
        f.Discover(Player1, Stone);
        ReachPlacement.MoveEyeToDistance(f, Player1, FunctionalBlocks.ChestCell, 19.5f);

        var applied = f.Accept(Player1, Craft("t:hand"));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(Bag, 0), Stone, 1, ItemSink.Craft),
                new ItemDestroyOp(new SlotRef(chest, 0), Stone, 1, ItemSink.Craft),
                new ItemCreateOp(new SlotRef(Bag, 0), Brick, 1, 500, ItemSource.Craft),
                new ItemDiscoverOp(Player1, Brick),
            ],
            compound.Components);
        Assert.True(f.Container(chest).GetSlot(0).IsEmpty);
    }

    [Fact]
    public void Hands_ChestBeyond20m_NotEnoughItems()
    {
        var f = Fixture();
        var chest = f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Stone, 1));
        f.Stock(Bag, 0, Stone, 1);
        f.Discover(Player1, Stone);

        // The same intent is refused 20.01 m from the chest and accepted 19.99 m from it: the distance is the only difference. The boundary
        // is inclusive (D13, the rule of K6-30 DECYZJA-8), checked 1 cm either side (review B1, 4).
        ReachPlacement.MoveEyeToDistance(f, Player1, FunctionalBlocks.ChestCell, 20.01f);
        f.AssertRejected(Player1, Craft("t:hand"), RejectReason.NotEnoughItems);
        Assert.Equal(new ItemStack(Stone, 1), f.Container(chest).GetSlot(0));

        ReachPlacement.MoveEyeToDistance(f, Player1, FunctionalBlocks.ChestCell, 19.99f);
        f.Accept(Player1, Craft("t:hand"));
    }

    [Fact]
    public void Workbench_ChestsNearestFirst_ThenById()
    {
        var f = WithBench();

        // The far chest is created first (lowest id), then (2,1,2) and (0,1,2), both 1 m from the bench: the order comes from the distance, then the id.
        var far = f.SetUpChestWith(new CellPos(6, 1, 2), Player1, ContainerAccess.Shared, (Brick, 1));
        var east = f.SetUpChestWith(new CellPos(2, 1, 2), Player1, ContainerAccess.Shared, (Brick, 1));
        var west = f.SetUpChestWith(new CellPos(0, 1, 2), Player1, ContainerAccess.Shared, (Brick, 1));

        var applied = f.Accept(Player1, Craft("t:bench1", 1, BenchCell));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(east, 0), Brick, 1, ItemSink.Craft),
                new ItemDestroyOp(new SlotRef(west, 0), Brick, 1, ItemSink.Craft),
                new ItemCreateOp(new SlotRef(Bag, 0), Gem, 1, 5, ItemSource.Craft),
                new ItemDiscoverOp(Player1, Gem),
            ],
            compound.Components);
        Assert.Equal(new ItemStack(Brick, 1), f.Container(far).GetSlot(0));
    }

    [Fact]
    public void Workbench_BagThenHotbarThenChests()
    {
        var f = WithBench();
        var chest = f.SetUpChestWith(new CellPos(2, 1, 2), Player1, ContainerAccess.Shared, (Brick, 2));
        f.Stock(Bag, 3, Brick, 1);
        f.Stock(Bag, 1, Brick, 1);
        f.Stock(Hotbar, 2, Brick, 1);

        var applied = f.Accept(Player1, Craft("t:bench1", 2, BenchCell));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(Bag, 1), Brick, 1, ItemSink.Craft),
                new ItemDestroyOp(new SlotRef(Bag, 3), Brick, 1, ItemSink.Craft),
                new ItemDestroyOp(new SlotRef(Hotbar, 2), Brick, 1, ItemSink.Craft),
                new ItemDestroyOp(new SlotRef(chest, 0), Brick, 1, ItemSink.Craft),
                new ItemCreateOp(new SlotRef(Bag, 0), Gem, 2, 5, ItemSource.Craft),
                new ItemDiscoverOp(Player1, Gem),
            ],
            compound.Components);
        Assert.Equal(new ItemStack(Brick, 1), f.Container(chest).GetSlot(0));
    }

    [Fact]
    public void Workbench_PrivateChestOfOther_Ignored()
    {
        var f = WithBench(players: 2);
        var chest = f.SetUpChestWith(new CellPos(2, 1, 2), Player2, ContainerAccess.Private, (Brick, 2));

        f.AssertRejected(Player1, Craft("t:bench1", 1, BenchCell), RejectReason.NotEnoughItems);

        // The valid twin: once the owner shares the chest, the same intent is accepted and uses it.
        f.Apply(new ContainerConfigureOp(chest, 4, ContainerAccess.Shared));
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));
        Assert.True(f.Container(chest).GetSlot(0).IsEmpty);
    }

    [Fact]
    public void Workbench_OwnPrivateChest_Used()
    {
        var f = WithBench();
        var chest = f.SetUpChestWith(new CellPos(2, 1, 2), Player1, ContainerAccess.Private, (Brick, 2));

        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));

        Assert.True(f.Container(chest).GetSlot(0).IsEmpty);
        Assert.Equal(new ItemStack(Gem, 1), f.Container(Bag).GetSlot(0));
    }

    [Fact]
    public void Workbench_NoStation_StationRequired()
    {
        var f = WithBench();
        f.Stock(Bag, 0, Brick, 4);
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));

        f.AssertRejected(Player1, Craft("t:bench1"), StationRequired);
    }

    [Fact]
    public void Workbench_WrongStationKind_StationRequired()
    {
        var f = WithBench();
        f.SetUpTimedStation(FurnaceCell);
        f.Stock(Bag, 0, Brick, 4);
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));

        f.AssertRejected(Player1, Craft("t:bench1", 1, FurnaceCell), StationRequired);
    }

    [Fact]
    public void Station_UnknownConstructEmptyCellAndOutOfBounds()
    {
        var f = WithBench();
        f.Stock(Bag, 0, Brick, 8);
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));

        f.AssertRejected(Player1, new CraftIntent("t:bench1", 1, new BlockRef(new EntityId(999), BenchCell)), RejectReason.UnknownConstruct);
        f.AssertRejected(Player1, Craft("t:bench1", 1, new CellPos(9, 1, 2)), RejectReason.CellEmpty);
        f.AssertRejected(Player1, Craft("t:bench1", 1, new CellPos(3000, 0, 0)), RejectReason.CellOutOfBounds);
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));
    }

    [Fact]
    public void Workbench_Level1ForLevel2Recipe_StationRequired()
    {
        var f = WithBench();
        f.Stock(Bag, 0, Brick, 4);
        f.Stock(Bag, 1, Ingot, 4);

        f.AssertRejected(Player1, Craft("t:bench2", 1, BenchCell), StationRequired);

        // The valid twin: an upgrade 1 m from the bench makes it level 2.
        f.EnsureBlock(new CellPos(1, 1, 1), CraftingBlocks.AnvilType);
        f.Accept(Player1, Craft("t:bench2", 1, BenchCell));
        Assert.Equal(1, f.CountIn(Bag, Plate));
    }

    [Fact]
    public void Workbench_TwoDistinctUpgrades_Level3()
    {
        var f = WithBench();
        f.Stock(Bag, 0, Ingot, 5);
        f.EnsureBlock(new CellPos(1, 1, 1), CraftingBlocks.AnvilType);

        f.AssertRejected(Player1, Craft("t:bench3", 1, BenchCell), StationRequired);

        f.EnsureBlock(new CellPos(0, 1, 1), CraftingBlocks.ToolrackType);
        f.Accept(Player1, Craft("t:bench3", 1, BenchCell));
        Assert.Equal(1, f.CountIn(Bag, FuelCell));
    }

    [Fact]
    public void Workbench_SameUpgradeTwice_Level2()
    {
        var f = WithBench();
        f.Stock(Bag, 0, Ingot, 5);
        f.Stock(Bag, 1, Brick, 2);
        f.EnsureBlock(new CellPos(1, 1, 1), CraftingBlocks.AnvilType);
        f.EnsureBlock(new CellPos(0, 1, 1), CraftingBlocks.AnvilType);

        f.Accept(Player1, Craft("t:bench2", 1, BenchCell));

        f.AssertRejected(Player1, Craft("t:bench3", 1, BenchCell), StationRequired);
    }

    [Fact]
    public void Workbench_UpgradeOnOtherConstructWithin5m_Counts()
    {
        var f = WithBench();
        f.Stock(Bag, 0, Brick, 2);
        f.Stock(Bag, 1, Ingot, 2);
        f.AssertRejected(Player1, Craft("t:bench2", 1, BenchCell), StationRequired);

        // An anvil on another construct, 3.5 m from the bench.
        f.AddConstruct(NearConstruct, FrameRef.Of(WorldFactory.HomePlanet), new System.Numerics.Vector3(1f, 4001f, 5.5f), new CellPos(0, 0, 0), CraftingBlocks.AnvilType);

        f.Accept(Player1, Craft("t:bench2", 1, BenchCell));
    }

    [Fact]
    public void Workbench_UpgradeBeyond5m_Ignored()
    {
        var f = WithBench();
        f.Stock(Bag, 0, Brick, 2);
        f.Stock(Bag, 1, Ingot, 2);

        // An anvil 6 m from the bench does not count; a grindstone 4 m from it does.
        f.EnsureBlock(new CellPos(7, 1, 2), CraftingBlocks.AnvilType);
        f.AssertRejected(Player1, Craft("t:bench2", 1, BenchCell), StationRequired);

        f.EnsureBlock(new CellPos(5, 1, 2), CraftingBlocks.GrindstoneType);
        f.Accept(Player1, Craft("t:bench2", 1, BenchCell));
    }

    [Fact]
    public void Station_Beyond8m_TooFarFromStation()
    {
        var f = WithBench();
        f.Stock(Bag, 0, Brick, 4);

        // The boundary is inclusive (D14), checked 1 cm either side (review B1, 4).
        ReachPlacement.MoveEyeToDistance(f, Player1, BenchCell, 8.01f);
        f.AssertRejected(Player1, Craft("t:bench1", 1, BenchCell), TooFarFromStation);

        ReachPlacement.MoveEyeToDistance(f, Player1, BenchCell, 7.99f);
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));
    }

    [Fact]
    public void Station_Beyond8mWithTooLowLevel_TooFarFromStation()
    {
        // Review B1, 3a: the reach is checked before the level.
        var f = WithBench();
        f.Stock(Bag, 0, Brick, 1);
        f.Stock(Bag, 1, Ingot, 1);

        ReachPlacement.MoveEyeToDistance(f, Player1, BenchCell, 8.5f);
        f.AssertRejected(Player1, Craft("t:bench2", 1, BenchCell), TooFarFromStation);

        ReachPlacement.MoveEyeToDistance(f, Player1, BenchCell, 7.5f);
        f.AssertRejected(Player1, Craft("t:bench2", 1, BenchCell), StationRequired);

        f.EnsureBlock(new CellPos(1, 1, 1), CraftingBlocks.AnvilType);
        f.Accept(Player1, Craft("t:bench2", 1, BenchCell));
    }

    [Fact]
    public void Station_WrongKindBeyond8m_StationRequired()
    {
        // Review B1, 3c: the kind of the station is checked before the reach.
        var f = WithBench();
        f.SetUpTimedStation(FurnaceCell);
        f.Stock(Bag, 0, Brick, 4);
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));

        ReachPlacement.MoveEyeToDistance(f, Player1, FurnaceCell, 8.5f);
        f.AssertRejected(Player1, Craft("t:bench1", 1, FurnaceCell), StationRequired);
    }

    [Fact]
    public void Workbench_LevelTooLowAndNoIngredients_StationRequired()
    {
        // Review B1, 3d: the station and its level are checked before the sources of the ingredients.
        var f = WithBench();

        f.AssertRejected(Player1, Craft("t:bench2", 1, BenchCell), StationRequired);

        f.EnsureBlock(new CellPos(1, 1, 1), CraftingBlocks.AnvilType);
        f.AssertRejected(Player1, Craft("t:bench2", 1, BenchCell), RejectReason.NotEnoughItems);
    }

    [Fact]
    public void Recipe_LockedWithoutStation_RecipeLocked()
    {
        // Review B1, 3b: the visibility of a recipe is checked before the station.
        var f = Fixture();
        f.Stock(Bag, 0, Plate, 1);
        f.Stock(Bag, 1, Ingot, 1);
        f.Discover(Player1, Plate, Ingot);

        f.AssertRejected(Player1, Craft("t:fab"), RecipeLocked);

        f.Apply(new ProgressKeyGrantOp(SurvivalContentFactory.Boss2Key));
        f.AssertRejected(Player1, Craft("t:fab"), StationRequired);

        f.EnsureBlock(BenchCell, CraftingBlocks.FabricatorType);
        f.Accept(Player1, Craft("t:fab", 1, BenchCell));
        Assert.Equal(1, f.CountIn(Bag, Module));
    }

    [Fact]
    public void Workbench_UpgradeAtRangeBoundary()
    {
        // Review B1, 4: an upgrade on another construct 5.01 m from the bench does not count, one 4.99 m from it does (D14, inclusive boundary).
        var f = WithBench();
        f.Stock(Bag, 0, Brick, 2);
        f.Stock(Bag, 1, Ingot, 2);
        var frame = FrameRef.Of(WorldFactory.HomePlanet);

        // The bench centre is (1.5, 4001.5, 2.5); the cell (0,0,0) of a construct with origin (1, 4001, z) has its centre 0.5 m above z.
        f.AddConstruct(NearConstruct, frame, new System.Numerics.Vector3(1f, 4001f, 2.5f + 5.01f - 0.5f), new CellPos(0, 0, 0), CraftingBlocks.AnvilType);
        f.AssertRejected(Player1, Craft("t:bench2", 1, BenchCell), StationRequired);

        f.AddConstruct(new EntityId(103), frame, new System.Numerics.Vector3(1f, 4001f, 2.5f + 4.99f - 0.5f), new CellPos(0, 0, 0), CraftingBlocks.AnvilType);
        f.Accept(Player1, Craft("t:bench2", 1, BenchCell));
    }

    [Fact]
    public void Workbench_TwoIngredients_DestroyOrderByRecipeThenSource()
    {
        // Review B1, 11: the ingredients are taken in the order of the recipe (brick, then ingot), each from its sources in order, though the ingot lies in the lower slot.
        var f = WithBench();
        f.EnsureBlock(new CellPos(1, 1, 1), CraftingBlocks.AnvilType);
        f.Stock(Bag, 0, Ingot, 1);
        f.Stock(Bag, 1, Brick, 1);

        var applied = f.Accept(Player1, Craft("t:bench2", 1, BenchCell));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(Bag, 1), Brick, 1, ItemSink.Craft),
                new ItemDestroyOp(new SlotRef(Bag, 0), Ingot, 1, ItemSink.Craft),
                new ItemCreateOp(new SlotRef(Bag, 0), Plate, 1, 500, ItemSource.Craft),
                new ItemDiscoverOp(Player1, Plate),
            ],
            compound.Components);
    }

    [Fact]
    public void Workbench_StationContainersAndEquipment_NotSources()
    {
        // Review B1, 13 (D13): the containers of a station and the equipment are no sources, a chest is.
        var f = WithBench();
        var (input, _) = f.SetUpTimedStation(FurnaceCell);
        f.Stock(input, 0, Brick, 2);
        f.Stock(WorldFactory.EquipmentOf(1), 0, Brick, 2);

        f.AssertRejected(Player1, Craft("t:bench1", 1, BenchCell), RejectReason.NotEnoughItems);

        f.SetUpChestWith(new CellPos(2, 1, 2), Player1, ContainerAccess.Shared, (Brick, 2));
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));
        Assert.Equal(2, f.CountIn(input, Brick));
        Assert.Equal(2, f.CountIn(WorldFactory.EquipmentOf(1), Brick));
    }

    [Fact]
    public void Station_OtherPlanet_TooFarFromStation()
    {
        var f = ForCrafting(secondPlanet: true);
        f.EnsureBlock(BenchCell, CraftingBlocks.WorkbenchType);
        f.AddConstruct(OtherPlanetConstruct, FrameRef.Of(WorldFactory.SecondPlanet), new System.Numerics.Vector3(0f, 4000f, 0f), new CellPos(0, 0, 0), CraftingBlocks.WorkbenchType);
        f.Discover(Player1, Brick);
        f.Stock(Bag, 0, Brick, 4);
        f.Accept(Player1, Craft("t:bench1", 1, BenchCell));

        f.AssertRejected(Player1, new CraftIntent("t:bench1", 1, new BlockRef(OtherPlanetConstruct, new CellPos(0, 0, 0))), TooFarFromStation);
    }

    [Fact]
    public void Recipe_NoIngredientDiscovered_RecipeLocked()
    {
        var f = Fixture();
        f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Stone, 2));

        // Items in a chest are not discovered by the player; the stone is not in any container of the player.
        f.AssertRejected(Player1, Craft("t:hand"), RecipeLocked);

        f.Discover(Player1, Stone);
        f.Accept(Player1, Craft("t:hand"));
    }

    [Fact]
    public void Recipe_KeyMissing_RecipeLocked()
    {
        var f = Fixture();
        f.EnsureBlock(BenchCell, CraftingBlocks.FabricatorType);
        f.Stock(Bag, 0, Plate, 1);
        f.Stock(Bag, 1, Ingot, 1);
        f.Discover(Player1, Plate, Ingot);

        f.AssertRejected(Player1, Craft("t:fab", 1, BenchCell), RecipeLocked);
        Assert.False(f.State.ProgressKeys.Contains(SurvivalContentFactory.Boss2Key));
    }

    [Fact]
    public void Recipe_KeyGranted_Accepted()
    {
        var f = Fixture();
        f.EnsureBlock(BenchCell, CraftingBlocks.FabricatorType);
        f.Stock(Bag, 0, Plate, 1);
        f.Stock(Bag, 1, Ingot, 1);
        f.Discover(Player1, Plate, Ingot);
        f.AssertRejected(Player1, Craft("t:fab", 1, BenchCell), RecipeLocked);

        f.Apply(new ProgressKeyGrantOp(SurvivalContentFactory.Boss2Key));
        f.Accept(Player1, Craft("t:fab", 1, BenchCell));

        Assert.Equal(1, f.CountIn(Bag, Module));
        Assert.Equal(0, f.CountIn(Bag, Plate));
    }

    [Fact]
    public void Result_BagFull_InventoryFull_NothingConsumed()
    {
        var f = Fixture();
        var chest = f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Stone, 2));
        for (var slot = 0; slot < WorldFactory.BagSize; slot++)
        {
            f.Stock(Bag, slot, Soil, 500);
        }

        f.Discover(Player1, Stone);

        // The result goes to the bag only: the hotbar is empty and does not help.
        f.AssertRejected(Player1, Craft("t:hand"), RejectReason.InventoryFull);
        Assert.Equal(new ItemStack(Stone, 2), f.Container(chest).GetSlot(0));

        f.Apply(new ItemDestroyOp(new SlotRef(Bag, 39), Soil, 500, ItemSink.Admin));
        f.Accept(Player1, Craft("t:hand"));
        Assert.Equal(new ItemStack(Brick, 1), f.Container(Bag).GetSlot(39));
    }

    [Fact]
    public void Result_FitsAfterIngredientsRemoved_Accepted()
    {
        var f = Fixture();
        for (var slot = 0; slot < WorldFactory.BagSize; slot++)
        {
            if (slot == 7)
            {
                f.Stock(Bag, slot, Stone, 2);
            }
            else
            {
                f.Stock(Bag, slot, Soil, 500);
            }
        }

        f.Discover(Player1, Stone);

        var applied = f.Accept(Player1, Craft("t:hand"));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(Bag, 7), Stone, 2, ItemSink.Craft),
                new ItemCreateOp(new SlotRef(Bag, 7), Brick, 1, 500, ItemSource.Craft),
                new ItemDiscoverOp(Player1, Brick),
            ],
            compound.Components);
    }

    [Fact]
    public void Count3_MultipliesIngredientsAndResult()
    {
        var f = Fixture();
        f.Stock(Bag, 0, Stone, 5);
        f.Discover(Player1, Stone);

        f.AssertRejected(Player1, Craft("t:hand", 3), RejectReason.NotEnoughItems);

        f.Stock(Bag, 1, Stone, 1);
        var applied = f.Accept(Player1, Craft("t:hand", 3));
        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(
            [
                new ItemDestroyOp(new SlotRef(Bag, 0), Stone, 5, ItemSink.Craft),
                new ItemDestroyOp(new SlotRef(Bag, 1), Stone, 1, ItemSink.Craft),
                new ItemCreateOp(new SlotRef(Bag, 0), Brick, 3, 500, ItemSource.Craft),
                new ItemDiscoverOp(Player1, Brick),
            ],
            compound.Components);
    }

    [Fact]
    public void Count51_InvalidParameters()
    {
        var f = Fixture();
        f.Stock(Bag, 0, Stone, 200);
        f.Discover(Player1, Stone);

        f.AssertRejected(Player1, Craft("t:hand", 51), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, Craft("t:hand", 0), RejectReason.InvalidParameters);
        f.Accept(Player1, Craft("t:hand", 50));
        Assert.Equal(50, f.CountIn(Bag, Brick));
    }

    [Fact]
    public void UnknownRecipe_InvalidParameters()
    {
        var f = Fixture();
        f.Stock(Bag, 0, Stone, 20);
        f.Discover(Player1, Stone);
        f.Accept(Player1, Craft("t:hand"));

        f.AssertRejected(Player1, Craft("t:nope"), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, Craft(string.Empty), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, Craft(new string('x', 65)), RejectReason.InvalidParameters);
    }

    [Fact]
    public void WithoutRecipesContent_InvalidParameters()
    {
        var withRecipes = Fixture();
        withRecipes.Stock(Bag, 0, Stone, 4);
        withRecipes.Discover(Player1, Stone);
        withRecipes.Accept(Player1, Craft("t:hand"));

        var f = ForCrafting(withRecipes: false);
        f.Stock(Bag, 0, Stone, 4);

        f.AssertRejected(Player1, Craft("t:hand"), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Craft_UnknownPlayerAndOffline_CheckedFirst()
    {
        var f = Fixture();
        f.Stock(Bag, 0, Stone, 4);
        f.Discover(Player1, Stone);
        f.Accept(Player1, Craft("t:hand"));

        Assert.Equal(RejectReason.UnknownPlayer, f.SubmitAs(new PlayerId(9), Craft("t:hand")).Reason);
        f.Sim.Sim.LeaveAllOnlinePlayers();
        Assert.Equal(RejectReason.PlayerOffline, f.Submit(Craft("t:hand")).Reason);
    }

    [Fact]
    public void Burst_ItemsBucket_RateLimited()
    {
        var f = Fixture();
        f.Stock(Bag, 0, Stone, 500);
        f.Discover(Player1, Stone);

        for (var i = 0; i < 40; i++)
        {
            var result = f.Submit(Craft("t:hand"));
            Assert.True(result.IsAccepted, $"craft {i + 1}: {result.Reason}");
        }

        f.AssertRejected(Player1, Craft("t:hand"), RejectReason.RateLimited);

        // 20 per second at 60 ticks per second: one item intent every 3 ticks.
        f.Wait(2);
        Assert.Equal(RejectReason.RateLimited, f.Submit(Craft("t:hand")).Reason);
        f.Wait(1);
        Assert.True(f.Submit(Craft("t:hand")).IsAccepted);
    }
}
