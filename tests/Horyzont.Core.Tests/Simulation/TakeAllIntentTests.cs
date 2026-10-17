using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// <see cref="TakeAllIntent"/> (intent 23, K6-30 D17, DO USTALENIA-2). The items land like mined ones: stacks of the hotbar, stacks of
/// the bag, empty bag slots, empty hotbar slots.
/// </summary>
public sealed class TakeAllIntentTests
{
    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
    private static readonly ItemTypeId _gem = WorldFactory.Item(SurvivalContentFactory.GemKey);
    private static readonly ItemTypeId _tool = WorldFactory.Item(SurvivalContentFactory.ToolKey);

    private static FunctionalBlockFixture Fixture(int players = 1) => ForIntents(players);

    private static ItemStack Slot(FunctionalBlockFixture f, ContainerId container, int slot) => f.Container(container).GetSlot((ushort)slot);

    private static void GiveGems(FunctionalBlockFixture f, ContainerId container, int slot, int count) =>
        f.Apply(new ItemCreateOp(new SlotRef(container, (ushort)slot), _gem, (ushort)count, 5, ItemSource.Admin));

    private static CompoundOp FillWithTools(ContainerId container, int firstSlot, int slots)
    {
        var ops = new Operation[slots];
        for (var i = 0; i < slots; i++)
        {
            ops[i] = new ItemCreateOp(new SlotRef(container, (ushort)(firstSlot + i)), _tool, 1, 1, ItemSource.Admin);
        }

        return new CompoundOp(ops);
    }

    [Fact]
    public void TakeAll_Chest_MovesToHotbarStacksThenBag()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        GiveGems(f, chest, 0, 3);
        f.Give(chest, 1, 10);
        GiveGems(f, Hotbar, 2, 1);
        f.Give(Bag, 5, 100);

        f.Accept(Player1, new TakeAllIntent(chest));

        Assert.Equal(new ItemStack(_gem, 4), Slot(f, Hotbar, 2));
        Assert.Equal(new ItemStack(_stone, 110), Slot(f, Bag, 5));
        Assert.True(Slot(f, chest, 0).IsEmpty);
        Assert.True(Slot(f, chest, 1).IsEmpty);
    }

    [Fact]
    public void TakeAll_PartialRoom_MovesWhatFits()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        GiveGems(f, chest, 0, 3);
        f.Give(chest, 1, 10);

        // One free slot is left in the bag and none in the hotbar: the first chest slot fills it, the second stays.
        f.Apply(FillWithTools(Hotbar, 0, WorldFactory.HotbarSize));
        f.Apply(FillWithTools(Bag, 1, WorldFactory.BagSize - 1));

        f.Accept(Player1, new TakeAllIntent(chest));

        Assert.Equal(new ItemStack(_gem, 3), Slot(f, Bag, 0));
        Assert.True(Slot(f, chest, 0).IsEmpty);
        Assert.Equal(new ItemStack(_stone, 10), Slot(f, chest, 1));
    }

    [Fact]
    public void TakeAll_NoRoom_InventoryFull()
    {
        var f = Fixture();
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        f.Give(chest, 0, 10);
        f.Apply(FillWithTools(Hotbar, 0, WorldFactory.HotbarSize));
        f.Apply(FillWithTools(Bag, 0, WorldFactory.BagSize));

        f.AssertRejected(Player1, new TakeAllIntent(chest), RejectReason.InventoryFull);
        Assert.Equal(new ItemStack(_stone, 10), Slot(f, chest, 0));
    }

    [Fact]
    public void TakeAll_PrivateChestOfOther_ContainerLocked()
    {
        var f = Fixture(players: 2);
        var shared = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player2);
        var privateChest = f.SetUpChestAt(new Horyzont.Core.World.Constructs.CellPos(4, 1, 0), Player2, ContainerAccess.Private);
        f.Give(shared, 0, 4);
        f.Give(privateChest, 0, 4);
        f.Accept(Player1, new TakeAllIntent(shared));

        f.AssertRejected(Player1, new TakeAllIntent(privateChest), RejectReason.ContainerLocked);
        Assert.Equal(new ItemStack(_stone, 4), Slot(f, privateChest, 0));
    }

    [Fact]
    public void TakeAll_WithoutSurvivalContent_InvalidParameters()
    {
        // Control: the same setup with survival content accepts the intent.
        var with = Fixture();
        var chest = with.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        with.Give(chest, 0, 3);
        with.Accept(Player1, new TakeAllIntent(chest));

        var without = ForIntents(1, withSurvival: false);
        var other = without.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        without.Give(other, 0, 3);

        without.AssertRejected(Player1, new TakeAllIntent(other), RejectReason.InvalidParameters);
    }
}
