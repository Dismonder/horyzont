using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Crafting.CraftingWorld;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// The automatic discovery of held items (K6-32 D9): with recipe content the simulation applies an <see cref="ItemDiscoverOp"/> as a separate
/// journal entry after each operation that put a new item type into a container of a player, and
/// <see cref="Core.Simulation.ServerSimulation.DiscoverHeldItems"/> does the same for what a player already holds. Without recipes nothing is discovered.
/// </summary>
public sealed class DiscoveryTests
{
    private static FunctionalBlockFixture Fixture(int players = 1, bool withRecipes = true) => ForCrafting(players, withRecipes);

    private static ItemMoveIntent Move(ContainerId from, int fromSlot, ContainerId to, int toSlot, int count, ItemTypeId type) =>
        new(new SlotRef(from, (ushort)fromSlot), ItemMoveTarget.ToSlot(new SlotRef(to, (ushort)toSlot)), (ushort)count, type, (ushort)count);

    [Fact]
    public void DigYield_NewType_DiscoverOpAfterwards()
    {
        var f = Fixture();
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(1, 3);
        var journaled = f.JournalCount();

        f.Accept(Player1, f.Sim.Dig(Player1, f.Sim.Ground(2f, 6f)));

        var ops = f.JournalFrom(journaled);
        Assert.Equal(2, ops.Count);
        Assert.IsType<CompoundOp>(ops[0]);
        Assert.Equal(new ItemDiscoverOp(Player1, Stone), ops[1]);
        Assert.True(f.IsDiscovered(Player1, Stone));
    }

    [Fact]
    public void MoveFromChest_NewType_Discovered()
    {
        var f = Fixture();
        var chest = f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Gem, 3));
        Assert.False(f.IsDiscovered(Player1, Gem));
        var journaled = f.JournalCount();

        f.Accept(Player1, Move(chest, 0, Bag, 0, 3, Gem));

        var ops = f.JournalFrom(journaled);
        Assert.Equal(2, ops.Count);
        Assert.IsType<ItemTransferOp>(ops[0]);
        Assert.Equal(new ItemDiscoverOp(Player1, Gem), ops[1]);
    }

    [Fact]
    public void CraftResult_DiscoveredInSameCompound()
    {
        var f = Fixture();
        f.Stock(Bag, 0, Stone, 2);
        f.Discover(Player1, Stone);
        var journaled = f.JournalCount();

        var applied = f.Accept(Player1, Craft("t:hand"));

        Assert.Equal(journaled + 1, f.JournalCount());
        Assert.Contains(new ItemDiscoverOp(Player1, Brick), Assert.IsType<CompoundOp>(applied.Operation).Components);
        Assert.True(f.IsDiscovered(Player1, Brick));
    }

    [Fact]
    public void KnownTypes_NoExtraOperation()
    {
        var f = Fixture();
        var chest = f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Gem, 3));
        f.Accept(Player1, Move(chest, 0, Bag, 0, 2, Gem));
        Assert.True(f.IsDiscovered(Player1, Gem));
        var journaled = f.JournalCount();

        f.Accept(Player1, Move(chest, 0, Bag, 0, 1, Gem));

        Assert.IsType<ItemTransferOp>(Assert.Single(f.JournalFrom(journaled)));
    }

    [Fact]
    public void WithoutRecipesContent_NoDiscoveryOps()
    {
        var f = Fixture(withRecipes: false);
        var chest = f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Gem, 3));
        f.Stock(Bag, 5, Stone, 4);
        var journaled = f.JournalCount();

        f.Accept(Player1, Move(chest, 0, Bag, 0, 3, Gem));

        Assert.IsType<ItemTransferOp>(Assert.Single(f.JournalFrom(journaled)));
        Assert.Equal(0, f.State.Progress.Count);
    }

    [Fact]
    public void DiscoveryOp_DoesNotRecurse()
    {
        var f = Fixture();
        var chest = f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Gem, 3));
        var applied = new List<Operation>();
        f.Sim.Sim.Applied += a => applied.Add(a.Operation);

        f.Accept(Player1, Move(chest, 0, Bag, 0, 3, Gem));

        Assert.Equal(2, applied.Count);
        Assert.IsType<ItemTransferOp>(applied[0]);
        Assert.Equal(new ItemDiscoverOp(Player1, Gem), applied[1]);
    }

    [Fact]
    public void Discovery_SeveralTypes_OneCompoundAscendingByType()
    {
        var f = Fixture();
        var journaled = f.JournalCount();

        f.Apply(new CompoundOp([Create(Bag, 0, Stone, 1), Create(Bag, 1, Gem, 1)]));

        var ops = f.JournalFrom(journaled);
        Assert.Equal(2, ops.Count);
        Assert.Equal(new CompoundOp([new ItemDiscoverOp(Player1, Gem), new ItemDiscoverOp(Player1, Stone)]), ops[1]);
    }

    [Fact]
    public void Discovery_OnlyContainersOfPlayers_ChestContentsIgnored()
    {
        var f = Fixture();
        var journaled = f.JournalCount();

        f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Gem, 3));

        Assert.DoesNotContain(f.JournalFrom(journaled), op => op is ItemDiscoverOp);
        Assert.False(f.IsDiscovered(Player1, Gem));

        // The twin (review B1, 14): the same item in the bag of the player is discovered, so the line above is a rule and not a missing feature.
        f.Stock(Bag, 0, Gem, 1);
        Assert.Equal(new ItemDiscoverOp(Player1, Gem), f.JournalFrom(journaled)[^1]);
        Assert.True(f.IsDiscovered(Player1, Gem));
    }

    [Fact]
    public void Discovery_ForTheOwnerOfTheContainer()
    {
        var f = Fixture(players: 2);
        var journaled = f.JournalCount();

        f.Stock(WorldFactory.BagOf(2), 0, Gem, 1);

        Assert.Equal(new ItemDiscoverOp(Player2, Gem), f.JournalFrom(journaled)[^1]);
        Assert.False(f.IsDiscovered(Player1, Gem));
    }

    [Fact]
    public void DiscoverHeldItems_StartKit_OneCompound()
    {
        var legacy = Fixture(withRecipes: false);
        legacy.Stock(Hotbar, 0, Tool, 1);
        legacy.Stock(Hotbar, 1, Gem, 3);
        legacy.Stock(Bag, 5, Stone, 4);
        Assert.Equal(0, legacy.State.Progress.Count);
        var sim = new SimFixture(state: legacy.State, content: CraftingBlocks.ContentFor(legacy.State), survival: SurvivalContentFactory.CreateTestWithRecipes());

        var applied = sim.Sim.DiscoverHeldItems(Player1);

        var compound = Assert.IsType<CompoundOp>(Assert.IsType<AppliedOperation>(applied).Operation);
        Assert.Equal([new ItemDiscoverOp(Player1, Tool), new ItemDiscoverOp(Player1, Gem), new ItemDiscoverOp(Player1, Stone)], compound.Components);
        Assert.Single(sim.Journal.Entries);
        Assert.Null(sim.Sim.DiscoverHeldItems(Player1));
        Assert.Single(sim.Journal.Entries);
    }

    [Fact]
    public void DiscoverHeldItems_OneType_PlainOperation()
    {
        var legacy = Fixture(withRecipes: false);
        legacy.Stock(Bag, 5, Stone, 4);
        var sim = new SimFixture(state: legacy.State, content: CraftingBlocks.ContentFor(legacy.State), survival: SurvivalContentFactory.CreateTestWithRecipes());

        var applied = sim.Sim.DiscoverHeldItems(Player1);

        Assert.Equal(new ItemDiscoverOp(Player1, Stone), Assert.IsType<AppliedOperation>(applied).Operation);
    }

    [Fact]
    public void DiscoverHeldItems_WithoutRecipesContent_Null()
    {
        var f = Fixture(withRecipes: false);
        f.Stock(Bag, 5, Stone, 4);
        var journaled = f.JournalCount();

        Assert.Null(f.Sim.Sim.DiscoverHeldItems(Player1));
        Assert.Equal(journaled, f.JournalCount());
    }
}
