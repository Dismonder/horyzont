using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Survival.HazardWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-34 D13: the equipment container (seven slots of <see cref="EquipmentLayout"/>) takes part in item moves and drops, and only in them: eating, refilling, crafting, quick stack and the
/// <c>AnyIn</c> target never take an item out of it or fill it by accident. In the test content only the modules (types 11 and 12, slot kind <see cref="Horyzont.Core.Content.EquipmentSlot.Module"/>)
/// can be worn. Every refusal first accepts a valid twin, because without the hazard content (and before step B4) every move to or from the equipment is refused and the refusal alone proves nothing.
/// </summary>
public sealed class EquipmentMoveTests
{
    private static SlotRef Eq(int slot) => new(EquipmentOf(Player1), (ushort)slot);

    private static ItemMoveIntent Move(SlotRef from, SlotRef to, ItemTypeId type, int stack = 1, int count = 1) =>
        new(from, ItemMoveTarget.ToSlot(to), (ushort)count, type, (ushort)stack);

    private static ItemMoveIntent MoveAny(SlotRef from, ContainerId to, ItemTypeId type, int stack = 1, int count = 1) =>
        new(from, ItemMoveTarget.AnyIn(to), (ushort)count, type, (ushort)stack);

    private static HazardWorld Worn()
    {
        var w = new HazardWorld(survival: true);
        w.Give(Player1, Tank, 1, bagSlot: 0);
        return w;
    }

    [Fact]
    public void ModuleIntoModuleSlot_Accepted()
    {
        var w = Worn();

        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        Assert.Equal(new ItemStack(Tank, 1), w.EquipmentOfPlayer(Player1)[4]);
        Assert.True(w.Blocks.Container(BagOf(Player1)).GetSlot(0).IsEmpty);
    }

    [Fact]
    public void ModuleIntoHelmetSlot_InvalidParameters()
    {
        var w = Worn();
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(5), Tank));
        w.Give(Player1, Thermal, 1, bagSlot: 1);

        // The helmet (0), the suit (1), the legs (2) and the backpack (3) slots take no module.
        foreach (var slot in new[] { 0, 1, 2, 3 })
        {
            w.Blocks.AssertRejected(Player1, Move(BagSlot(Player1, 1), Eq(slot), Thermal), RejectReason.InvalidParameters);
        }
    }

    [Fact]
    public void NonWearableIntoEquipment_InvalidParameters()
    {
        var w = Worn();
        w.Give(Player1, Stone, 5, bagSlot: 1);
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        w.Blocks.AssertRejected(Player1, Move(BagSlot(Player1, 1), Eq(5), Stone, 5, 1), RejectReason.InvalidParameters);
        Assert.True(w.EquipmentOfPlayer(Player1)[5].IsEmpty);
    }

    [Fact]
    public void SwapBringsWrongItemIntoEquipment_InvalidParameters()
    {
        var w = Worn();
        w.Give(Player1, Stone, 5, bagSlot: 1);
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        // A swap puts the item of the target where the moved item came from: here a stone into the module slot.
        w.Blocks.AssertRejected(Player1, Move(Eq(4), BagSlot(Player1, 1), Tank), RejectReason.InvalidParameters);
        Assert.Equal(new ItemStack(Tank, 1), w.EquipmentOfPlayer(Player1)[4]);
        Assert.Equal(new ItemStack(Stone, 5), w.Blocks.Container(BagOf(Player1)).GetSlot(1));
    }

    [Fact]
    public void SwapWithinEquipment_ChecksBothSlots()
    {
        var w = Worn();
        w.Equip(Player1, 5, Thermal);
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        // Two module slots swap their modules.
        w.Blocks.Accept(Player1, Move(Eq(4), Eq(5), Tank));
        Assert.Equal(new ItemStack(Tank, 1), w.EquipmentOfPlayer(Player1)[5]);
        Assert.Equal(new ItemStack(Thermal, 1), w.EquipmentOfPlayer(Player1)[4]);

        // A module put straight into the helmet slot (a server operation, no layout check) must not travel between the slots: the move of the module of slot 4 to slot 0
        // fails for the moved item, and the move of slot 0 to slot 4 fails for the item that would come back to the helmet slot.
        w.Equip(Player1, 0, Tank);
        w.Blocks.AssertRejected(Player1, Move(Eq(4), Eq(0), Thermal), RejectReason.InvalidParameters);
        w.Blocks.AssertRejected(Player1, Move(Eq(0), Eq(4), Tank), RejectReason.InvalidParameters);
    }

    [Fact]
    public void TransferCountTwoIntoEquipment_InvalidParameters()
    {
        var w = new HazardWorld(survival: true);
        w.Give(Player1, Tank, 2, bagSlot: 0);

        // The limit of a stack in the equipment is one (D13): two at once are refused, one is taken.
        w.Blocks.AssertRejected(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank, 2, 2), RejectReason.InvalidParameters);
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank, 2, 1));
        Assert.Equal(new ItemStack(Tank, 1), w.EquipmentOfPlayer(Player1)[4]);
    }

    [Fact]
    public void AnyInEquipment_FirstAcceptingEmptySlot()
    {
        var w = Worn();
        w.Give(Player1, Thermal, 1, bagSlot: 1);

        // The module slots are 4 to 6; slots 0 to 3 do not accept a module, so the first accepting empty slot is 4, then 5.
        w.Blocks.Accept(Player1, MoveAny(BagSlot(Player1, 0), EquipmentOf(Player1), Tank));
        w.Blocks.Accept(Player1, MoveAny(BagSlot(Player1, 1), EquipmentOf(Player1), Thermal));

        Assert.Equal(new ItemStack(Tank, 1), w.EquipmentOfPlayer(Player1)[4]);
        Assert.Equal(new ItemStack(Thermal, 1), w.EquipmentOfPlayer(Player1)[5]);
        Assert.True(w.EquipmentOfPlayer(Player1)[0].IsEmpty);
    }

    [Fact]
    public void AnyInEquipment_NoSlot_InventoryFull()
    {
        var w = new HazardWorld(survival: true);
        w.Give(Player1, Tank, 1, bagSlot: 0);
        w.Give(Player1, Tank, 1, bagSlot: 1);
        w.Give(Player1, Thermal, 1, bagSlot: 2);
        w.Give(Player1, Thermal, 1, bagSlot: 3);
        w.Blocks.Accept(Player1, MoveAny(BagSlot(Player1, 0), EquipmentOf(Player1), Tank));
        w.Blocks.Accept(Player1, MoveAny(BagSlot(Player1, 1), EquipmentOf(Player1), Tank));
        w.Blocks.Accept(Player1, MoveAny(BagSlot(Player1, 2), EquipmentOf(Player1), Thermal));

        // The three module slots are taken: a fourth module has no place, and nothing is swapped out.
        w.Blocks.AssertRejected(Player1, MoveAny(BagSlot(Player1, 3), EquipmentOf(Player1), Thermal), RejectReason.InventoryFull);
    }

    [Fact]
    public void AnyInFromChest_NeverFillsEquipment()
    {
        var w = new HazardWorld(survival: true);
        w.Give(Player1, Tank, 1, bagSlot: 0);
        w.Blocks.Accept(Player1, MoveAny(BagSlot(Player1, 0), EquipmentOf(Player1), Tank));
        var chest = w.Blocks.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        w.Apply(new ItemCreateOp(new SlotRef(chest, 0), Thermal, 1, 1, ItemSource.Admin));

        // A module taken out of a chest to "any slot" of the bag lands in the bag; the equipment is filled only by a move aimed at it.
        w.Blocks.Accept(Player1, MoveAny(new SlotRef(chest, 0), BagOf(Player1), Thermal));

        Assert.Equal(new ItemStack(Thermal, 1), w.Blocks.Container(BagOf(Player1)).GetSlot(0));
        Assert.True(w.EquipmentOfPlayer(Player1)[5].IsEmpty);
    }

    [Fact]
    public void QuickStack_SkipsEquipment()
    {
        var w = Worn();
        var chest = w.Blocks.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        w.Apply(new ItemCreateOp(new SlotRef(chest, 0), Tank, 1, 1, ItemSource.Admin));
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        // The worn module is not "put away" into the chest that holds the same type: the bag is empty, so there is nothing to stack.
        w.Blocks.AssertRejected(Player1, new QuickStackIntent(), RejectReason.NotEnoughItems);
        Assert.Equal(new ItemStack(Tank, 1), w.EquipmentOfPlayer(Player1)[4]);

        // The accepted counterpart: the same module in the bag goes to the chest.
        w.Give(Player1, Tank, 1, bagSlot: 1);
        w.Blocks.Accept(Player1, new QuickStackIntent());
        Assert.Equal(new ItemStack(Tank, 1), w.EquipmentOfPlayer(Player1)[4]);
    }

    [Fact]
    public void Craft_DoesNotUseEquipment()
    {
        var w = new HazardWorld(survival: true);
        w.Give(Player1, Stone, 2, bagSlot: 0);
        w.Blocks.Accept(Player1, new CraftIntent(HazardTestContent.CutRecipe.Key, 1, null));

        // The same stones in an equipment slot (a server operation) are not an ingredient.
        var other = new HazardWorld(survival: true);
        other.Equip(Player1, 4, Stone);
        other.Equip(Player1, 5, Stone);
        other.Blocks.AssertRejected(Player1, new CraftIntent(HazardTestContent.CutRecipe.Key, 1, null), RejectReason.NotEnoughItems);
    }

    [Fact]
    public void Eat_FromEquipment_InvalidParameters()
    {
        var w = new HazardWorld(survival: true);
        w.Give(Player1, WorldFactory.Item(SurvivalContentFactory.BerriesKey), 1, bagSlot: 0);
        w.Blocks.Accept(Player1, new EatIntent(BagOf(Player1), 0));

        var other = new HazardWorld(survival: true);
        other.Equip(Player1, 4, WorldFactory.Item(SurvivalContentFactory.BerriesKey));
        other.Blocks.AssertRejected(Player1, new EatIntent(EquipmentOf(Player1), 4), RejectReason.InvalidParameters);
    }

    [Fact]
    public void EquipmentToBag_Accepted()
    {
        var w = Worn();
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        w.Blocks.Accept(Player1, Move(Eq(4), BagSlot(Player1, 7), Tank));

        Assert.True(w.EquipmentOfPlayer(Player1)[4].IsEmpty);
        Assert.Equal(new ItemStack(Tank, 1), w.Blocks.Container(BagOf(Player1)).GetSlot(7));
    }

    [Fact]
    public void BackpackSlot_InvalidParameters()
    {
        var w = Worn();
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        // The backpack slot (3) refuses until the package that resizes the bag (DO USTALENIA-5).
        w.Blocks.AssertRejected(Player1, Move(Eq(4), Eq(3), Tank), RejectReason.InvalidParameters);
    }

    [Fact]
    public void WithoutHazardContent_EquipmentStillInvalid()
    {
        var with = Worn();
        with.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        var without = new HazardWorld(survival: true, hazardContent: false);
        without.Give(Player1, Tank, 1, bagSlot: 0);
        without.Blocks.AssertRejected(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank), RejectReason.InvalidParameters);
    }

    [Fact]
    public void DropFromEquipment_Accepted()
    {
        var w = Worn();
        w.Blocks.Accept(Player1, Move(BagSlot(Player1, 0), Eq(4), Tank));

        w.Blocks.Accept(Player1, new ItemDropIntent(Eq(4), 1, Tank, 1));

        Assert.True(w.EquipmentOfPlayer(Player1)[4].IsEmpty);
    }
}
