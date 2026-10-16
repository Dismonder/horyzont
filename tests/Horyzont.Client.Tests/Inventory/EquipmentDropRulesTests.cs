using Horyzont.Client.Inventory;
using Horyzont.Client.Tests.Survival;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Inventory;

/// <summary>
/// What the client sends into the equipment (K6-34 D13, D20): a module goes to a module slot (4 to 6), never to the helmet, the torso, the legs or the backpack, and nothing goes there at all unless the
/// hazard content turned the equipment on. A move the server would refuse (<c>InvalidParameters</c>) is not sent. The bag of the fixture holds a tank in slot 0, a thermal module in slot 1 and stone in slot 2.
/// </summary>
public sealed class EquipmentDropRulesTests
{
    private static readonly SlotRef _bagTank = new(HazardClientFixture.BagId, 0);
    private static readonly SlotRef _bagThermal = new(HazardClientFixture.BagId, 1);
    private static readonly SlotRef _bagStone = new(HazardClientFixture.BagId, 2);

    private readonly HazardClientFixture _fx = new(withTank: false);
    private readonly InventoryFixture.RecordingSender _sender = new();
    private readonly InventoryInteraction _interaction;

    public EquipmentDropRulesTests()
    {
        _interaction = new InventoryInteraction(_fx.Inventory, _sender, NullLogger<InventoryInteraction>.Instance) { EquipmentEnabled = true };
        _fx.Put(HazardClientFixture.BagId, 0, HazardClientFixture.Tank, 1);
        _fx.Put(HazardClientFixture.BagId, 1, HazardClientFixture.Thermal, 1);
        _fx.Put(HazardClientFixture.BagId, 2, HazardClientFixture.Stone, 1);
    }

    private static SlotRef Equipment(int slot) => new(HazardClientFixture.EquipmentId, (ushort)slot);

    private InteractionOutcome Drop(SlotRef from, SlotRef to)
    {
        Assert.Equal(InteractionOutcome.Picked, _interaction.LeftClick(from));
        return _interaction.LeftClick(to);
    }

    [Fact]
    public void ModuleOntoModuleSlot_Sends()
    {
        Assert.Equal(InteractionOutcome.Sent, Drop(_bagTank, Equipment(4)));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(_sender.Intents));
        Assert.Equal(_bagTank, move.From);
        Assert.Equal(ItemMoveTarget.ToSlot(Equipment(4)), move.To);
        Assert.Equal<ushort>(1, move.Count);
    }

    [Fact]
    public void ModuleOntoHelmet_NotSent()
    {
        // The twin first: the same module goes to a module slot.
        Assert.Equal(InteractionOutcome.Sent, Drop(_bagThermal, Equipment(5)));
        _sender.Intents.Clear();

        foreach (var slot in new[] { 0, 1, 2 })
        {
            Assert.Equal(InteractionOutcome.Picked, _interaction.LeftClick(_bagTank));
            Assert.Equal(InteractionOutcome.Ignored, _interaction.LeftClick(Equipment(slot)));
            _interaction.Cancel();
        }

        Assert.Empty(_sender.Intents);
    }

    [Fact]
    public void AnythingOntoBackpack_NotSent()
    {
        Assert.Equal(InteractionOutcome.Picked, _interaction.LeftClick(_bagTank));
        Assert.Equal(InteractionOutcome.Ignored, _interaction.LeftClick(Equipment(3)));
        _interaction.Cancel();
        Assert.Equal(InteractionOutcome.Picked, _interaction.LeftClick(_bagStone));
        Assert.Equal(InteractionOutcome.Ignored, _interaction.LeftClick(Equipment(3)));

        Assert.Empty(_sender.Intents);
    }

    [Fact]
    public void StoneOntoModuleSlot_NotSent()
    {
        Assert.Equal(InteractionOutcome.Picked, _interaction.LeftClick(_bagStone));

        Assert.Equal(InteractionOutcome.Ignored, _interaction.LeftClick(Equipment(4)));
        Assert.Empty(_sender.Intents);
    }

    [Fact]
    public void ModuleOutOfTheEquipment_IntoTheBag_Sends()
    {
        _fx.Put(HazardClientFixture.EquipmentId, 4, HazardClientFixture.Tank, 1);

        Assert.Equal(InteractionOutcome.Sent, Drop(Equipment(4), new SlotRef(HazardClientFixture.BagId, 10)));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(_sender.Intents));
        Assert.Equal(Equipment(4), move.From);
    }

    [Fact]
    public void SwapWithAModule_ModuleComesBack_Sends()
    {
        _fx.Put(HazardClientFixture.EquipmentId, 4, HazardClientFixture.Tank, 1);

        // The tank held from the equipment goes onto the thermal module in the bag: the thermal module comes back into slot 4, a module slot, so the swap is sent.
        Assert.Equal(InteractionOutcome.Sent, Drop(Equipment(4), _bagThermal));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(_sender.Intents));
        Assert.Equal(Equipment(4), move.From);
        Assert.Equal(ItemMoveTarget.ToSlot(_bagThermal), move.To);
    }

    [Fact]
    public void SwapWithStone_StoneCannotComeBack_NotSent()
    {
        _fx.Put(HazardClientFixture.EquipmentId, 4, HazardClientFixture.Tank, 1);

        // The tank onto the stone would bring the stone back into a module slot, which the server refuses.
        Assert.Equal(InteractionOutcome.Picked, _interaction.LeftClick(Equipment(4)));
        Assert.Equal(InteractionOutcome.Ignored, _interaction.LeftClick(_bagStone));
        Assert.Empty(_sender.Intents);
    }

    [Fact]
    public void EquipmentOff_NothingIsUsable()
    {
        var off = new InventoryInteraction(_fx.Inventory, _sender, NullLogger<InventoryInteraction>.Instance);
        _fx.Put(HazardClientFixture.EquipmentId, 4, HazardClientFixture.Tank, 1);

        // Without the hazard content the equipment is not part of the screen: it can be neither taken from nor put into.
        Assert.Equal(InteractionOutcome.Ignored, off.LeftClick(Equipment(4)));
        Assert.Equal(InteractionOutcome.Picked, off.LeftClick(_bagThermal));
        Assert.Equal(InteractionOutcome.Ignored, off.LeftClick(Equipment(5)));
        Assert.Empty(_sender.Intents);
    }

    [Fact]
    public void CanPlace_TheRuleItself()
    {
        Assert.True(_fx.Inventory.TryGetDef(HazardClientFixture.Tank, out var tank));
        Assert.True(_fx.Inventory.TryGetDef(HazardClientFixture.Stone, out var stone));

        Assert.True(EquipmentDropRules.CanPlace(4, tank, 1));
        Assert.True(EquipmentDropRules.CanPlace(6, tank, 1));
        Assert.False(EquipmentDropRules.CanPlace(3, tank, 1));
        Assert.False(EquipmentDropRules.CanPlace(0, tank, 1));
        Assert.False(EquipmentDropRules.CanPlace(4, stone, 1));
        Assert.False(EquipmentDropRules.CanPlace(4, tank, 2));
        Assert.False(EquipmentDropRules.CanPlace(4, null, 1));
        Assert.False(EquipmentDropRules.CanPlace(7, tank, 1));
    }
}
