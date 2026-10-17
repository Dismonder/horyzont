using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// <see cref="QuickStackIntent"/> (intent 22, K6-30 D16). Player 1 stands 3 m from the chest block of the fixture (nearer) and 3.7 m
/// from a second chest block at <see cref="_farChest"/>; both are inside the 10 m of <see cref="GameRules.QuickStackRangeMeters"/>.
/// </summary>
public sealed class QuickStackIntentTests
{
    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
    private static readonly ItemTypeId _gem = WorldFactory.Item(SurvivalContentFactory.GemKey);
    private static readonly CellPos _nearChest = FunctionalBlocks.ChestCell;
    private static readonly CellPos _farChest = new(4, 1, 0);

    private static FunctionalBlockFixture Fixture(int players = 1) => ForIntents(players);

    private static ItemStack Slot(FunctionalBlockFixture f, ContainerId container, int slot) => f.Container(container).GetSlot((ushort)slot);

    private static void GiveGems(FunctionalBlockFixture f, ContainerId container, int slot, int count) =>
        f.Apply(new ItemCreateOp(new SlotRef(container, (ushort)slot), _gem, (ushort)count, 5, ItemSource.Admin));

    [Fact]
    public void QuickStack_MatchingTypes_NearestChestFirst()
    {
        var f = Fixture();

        // The far chest is created first, so it has the lower id: the order has to come from the distance.
        var far = f.SetUpChestAt(_farChest, Player1);
        var near = f.SetUpChestAt(_nearChest, Player1);
        f.Give(near, 0, 5);
        f.Give(far, 0, 5);
        f.Give(Bag, 0, 10);

        f.Accept(Player1, new QuickStackIntent());

        Assert.Equal(new ItemStack(_stone, 15), Slot(f, near, 0));
        Assert.Equal(new ItemStack(_stone, 5), Slot(f, far, 0));
        Assert.True(Slot(f, Bag, 0).IsEmpty);
    }

    [Fact]
    public void QuickStack_TopsUpStacksThenEmptySlots()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(_nearChest, Player1);
        f.Give(chest, 0, 495);
        f.Give(Bag, 0, 20);

        var applied = f.Accept(Player1, new QuickStackIntent());

        Assert.Equal(
            [
                new ItemTransferOp(new SlotRef(Bag, 0), new SlotRef(chest, 0), _stone, 5, 500),
                new ItemTransferOp(new SlotRef(Bag, 0), new SlotRef(chest, 1), _stone, 15, 500),
            ],
            Flatten(applied.Operation));
        Assert.Equal(new ItemStack(_stone, 500), Slot(f, chest, 0));
        Assert.Equal(new ItemStack(_stone, 15), Slot(f, chest, 1));
        Assert.True(Slot(f, Bag, 0).IsEmpty);
    }

    [Fact]
    public void QuickStack_HotbarUntouched()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(_nearChest, Player1);
        f.Give(chest, 0, 1);
        f.Give(Hotbar, 3, 7);
        f.Give(Bag, 0, 4);

        f.Accept(Player1, new QuickStackIntent());

        Assert.Equal(new ItemStack(_stone, 7), Slot(f, Hotbar, 3));
        Assert.Equal(new ItemStack(_stone, 5), Slot(f, chest, 0));
        Assert.True(Slot(f, Bag, 0).IsEmpty);
    }

    [Fact]
    public void QuickStack_ChestBeyond10m_Ignored()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(_nearChest, Player1);
        f.Give(chest, 0, 1);
        f.Give(Bag, 0, 5);
        f.Accept(Player1, new QuickStackIntent());
        f.Give(Bag, 0, 5);
        f.Move(Player1, new Vector3(2f, 4001f, 14f));

        f.AssertRejected(Player1, new QuickStackIntent(), RejectReason.NotEnoughItems);
        Assert.Equal(new ItemStack(_stone, 6), Slot(f, chest, 0));
        Assert.Equal(new ItemStack(_stone, 5), Slot(f, Bag, 0));
    }

    [Fact]
    public void QuickStack_PrivateChestOfOther_Ignored()
    {
        var f = Fixture(players: 2);
        var closed = f.SetUpChestAt(_nearChest, Player2, ContainerAccess.Private);
        var open = f.SetUpChestAt(_farChest, Player2);
        f.Give(closed, 0, 1);
        f.Give(open, 0, 1);
        f.Give(Bag, 0, 10);

        f.Accept(Player1, new QuickStackIntent());

        Assert.Equal(new ItemStack(_stone, 1), Slot(f, closed, 0));
        Assert.Equal(new ItemStack(_stone, 11), Slot(f, open, 0));
        Assert.True(Slot(f, Bag, 0).IsEmpty);
    }

    [Fact]
    public void QuickStack_NoMatchingItems_NotEnoughItems()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(_nearChest, Player1);
        f.Give(chest, 0, 5);
        GiveGems(f, Bag, 0, 2);

        f.AssertRejected(Player1, new QuickStackIntent(), RejectReason.NotEnoughItems);
        Assert.Equal(new ItemStack(_gem, 2), Slot(f, Bag, 0));
    }

    [Fact]
    public void QuickStack_ChestsFull_InventoryFull()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(_nearChest, Player1);
        f.Give(chest, 0, 500);
        for (var slot = 1; slot < 4; slot++)
        {
            GiveGems(f, chest, slot, 1);
        }

        f.Give(Bag, 0, 10);

        f.AssertRejected(Player1, new QuickStackIntent(), RejectReason.InventoryFull);
        Assert.Equal(new ItemStack(_stone, 10), Slot(f, Bag, 0));
    }

    [Fact]
    public void QuickStack_SameState_SameOperation()
    {
        var first = Run();
        var second = Run();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);

        static IReadOnlyList<Operation> Run()
        {
            var f = Fixture();
            var near = f.SetUpChestAt(_nearChest, Player1);
            var far = f.SetUpChestAt(_farChest, Player1);
            f.Give(near, 0, 498);
            f.Give(far, 1, 100);
            f.Give(Bag, 0, 30);
            f.Give(Bag, 5, 60);
            GiveGems(f, Bag, 2, 3);
            return Flatten(f.Accept(Player1, new QuickStackIntent()).Operation);
        }
    }

    [Fact]
    public void QuickStack_ChestAtRangeBoundary_IncludedJustBeyondIgnored()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(_nearChest, Player1);
        f.Give(chest, 0, 1);
        f.Give(Bag, 0, 5);

        ReachPlacement.MoveEyeToDistance(f, Player1, _nearChest, 10.01f);
        f.AssertRejected(Player1, new QuickStackIntent(), RejectReason.NotEnoughItems);

        ReachPlacement.MoveEyeToDistance(f, Player1, _nearChest, 9.99f);
        f.Accept(Player1, new QuickStackIntent());

        Assert.Equal(new ItemStack(_stone, 6), Slot(f, chest, 0));
        Assert.True(Slot(f, Bag, 0).IsEmpty);
    }

    [Fact]
    public void QuickStack_EqualDistance_LowerContainerIdFirst()
    {
        var f = Fixture();

        // The chests at x = +0.5 and x = -0.5 are exactly as far from a player at x = 0 (up is +Y there), so the distance ties. The
        // chest of the lower id sits in the cell that sorts later, so neither the cell order nor the creation of the second can win it.
        var lowerId = f.SetUpChestAt(_nearChest, Player1);
        var higherId = f.SetUpChestAt(new CellPos(-1, 1, 0), Player1);
        Assert.True(lowerId.Value < higherId.Value);
        f.Give(lowerId, 0, 1);
        f.Give(higherId, 0, 1);
        f.Give(Bag, 0, 5);
        f.Move(Player1, new Vector3(0f, 4001f, 3f));
        Assert.Equal(0f, f.Sim.Eye(Player1).X);

        f.Accept(Player1, new QuickStackIntent());

        Assert.Equal(new ItemStack(_stone, 6), Slot(f, lowerId, 0));
        Assert.Equal(new ItemStack(_stone, 1), Slot(f, higherId, 0));
    }

    [Fact]
    public void QuickStack_WithoutSurvivalContent_InvalidParameters()
    {
        // Control: the same setup with survival content accepts the intent.
        var with = Fixture();
        var chest = with.SetUpChestAt(_nearChest, Player1);
        with.Give(chest, 0, 1);
        with.Give(Bag, 0, 5);
        with.Accept(Player1, new QuickStackIntent());

        var without = ForIntents(1, withSurvival: false);
        var other = without.SetUpChestAt(_nearChest, Player1);
        without.Give(other, 0, 1);
        without.Give(Bag, 0, 5);

        without.AssertRejected(Player1, new QuickStackIntent(), RejectReason.InvalidParameters);
    }
}
