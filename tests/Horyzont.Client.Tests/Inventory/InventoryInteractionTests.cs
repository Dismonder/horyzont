using Horyzont.Client.Inventory;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Tests.Inventory;

public sealed class InventoryInteractionTests
{
    private static readonly ItemTypeId _stone = InventoryFixture.Stone;
    private static readonly ItemTypeId _soil = InventoryFixture.Soil;

    private static InventoryFixture Rig()
    {
        var fx = new InventoryFixture();
        fx.Put(InventoryFixture.Bag(3), _stone, 12);
        fx.Put(InventoryFixture.Bag(4), _stone, 6);
        fx.Put(InventoryFixture.Bag(5), _soil, 9);
        return fx;
    }

    private static IntentReply Reply(InventoryFixture fx, RejectReason reason = RejectReason.None) => new(fx.Sender.LastSeq, reason, default);

    [Fact]
    public void LeftClick_PickWhole_PlaceEmpty_Transfer()
    {
        var fx = Rig();

        Assert.Equal(InteractionOutcome.Picked, fx.Interaction.LeftClick(InventoryFixture.Bag(3)));
        Assert.Equal(new HeldStack(InventoryFixture.Bag(3), _stone, 12, 12), fx.Interaction.Held);
        Assert.Empty(fx.Sender.Intents);

        Assert.Equal(InteractionOutcome.Sent, fx.Interaction.LeftClick(InventoryFixture.Hotbar(7)));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(fx.Sender.Intents));
        Assert.Equal(InventoryFixture.Bag(3), move.From);
        Assert.Equal(ItemMoveTarget.ToSlot(InventoryFixture.Hotbar(7)), move.To);
        Assert.Equal<ushort>(12, move.Count);
        Assert.Equal(_stone, move.ExpectedType);
        Assert.Equal<ushort>(12, move.ExpectedCount);
        Assert.Null(fx.Interaction.Held);
    }

    [Fact]
    public void RightClick_PickHalf_Split()
    {
        var fx = Rig();

        Assert.Equal(InteractionOutcome.Picked, fx.Interaction.RightClick(InventoryFixture.Bag(4)));
        Assert.Equal<ushort>(3, fx.Interaction.Held!.Value.Count);
        fx.Interaction.LeftClick(InventoryFixture.Bag(10));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(fx.Sender.Intents));
        Assert.Equal<ushort>(3, move.Count);
        Assert.Equal<ushort>(6, move.ExpectedCount);
        Assert.Equal(_stone, move.ExpectedType);
    }

    [Fact]
    public void RightClick_OddStack_PicksTheLargerHalf_SingleItemPicksOne()
    {
        var fx = Rig();
        fx.Put(InventoryFixture.Bag(8), _stone, 7);
        fx.Put(InventoryFixture.Bag(9), _stone, 1);

        fx.Interaction.RightClick(InventoryFixture.Bag(8));
        Assert.Equal<ushort>(4, fx.Interaction.Held!.Value.Count);
        fx.Interaction.Cancel();
        fx.Interaction.RightClick(InventoryFixture.Bag(9));
        Assert.Equal<ushort>(1, fx.Interaction.Held!.Value.Count);
    }

    [Fact]
    public void Place_OnSameType_Merge()
    {
        var fx = Rig();

        fx.Interaction.LeftClick(InventoryFixture.Bag(3));
        fx.Interaction.LeftClick(InventoryFixture.Bag(4));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(fx.Sender.Intents));
        Assert.Equal(ItemMoveTarget.ToSlot(InventoryFixture.Bag(4)), move.To);
        Assert.Equal<ushort>(12, move.Count);
    }

    [Fact]
    public void Place_OnOtherType_Swap()
    {
        var fx = Rig();

        fx.Interaction.LeftClick(InventoryFixture.Bag(3));
        fx.Interaction.LeftClick(InventoryFixture.Bag(5));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(fx.Sender.Intents));
        Assert.Equal(ItemMoveTarget.ToSlot(InventoryFixture.Bag(5)), move.To);
        Assert.Equal<ushort>(12, move.Count); // the whole source: the server turns it into a swap
        Assert.Equal<ushort>(12, move.ExpectedCount);
    }

    [Fact]
    public void Place_PartOfAStackOnOtherType_IsNotSent()
    {
        var fx = Rig();

        fx.Interaction.RightClick(InventoryFixture.Bag(3));
        var outcome = fx.Interaction.LeftClick(InventoryFixture.Bag(5));

        Assert.Equal(InteractionOutcome.Ignored, outcome);
        Assert.Empty(fx.Sender.Intents);
        Assert.NotNull(fx.Interaction.Held);
    }

    [Fact]
    public void RightClick_WhileHolding_PlacesOneOnEmptyOrSameType()
    {
        var fx = Rig();

        fx.Interaction.LeftClick(InventoryFixture.Bag(3));
        fx.Interaction.RightClick(InventoryFixture.Bag(20));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(fx.Sender.Intents));
        Assert.Equal<ushort>(1, move.Count);
        Assert.Equal<ushort>(12, move.ExpectedCount);
    }

    [Fact]
    public void ShiftClick_MoveAny()
    {
        var fx = Rig();

        Assert.Equal(InteractionOutcome.Sent, fx.Interaction.ShiftClick(InventoryFixture.Bag(3)));
        var toHotbar = Assert.IsType<ItemMoveIntent>(fx.Sender.Intents[0]);
        Assert.Equal(ItemMoveTarget.AnyIn(InventoryFixture.HotbarId), toHotbar.To);
        Assert.Equal<ushort>(12, toHotbar.Count);
        fx.Interaction.OnIntentReply(Reply(fx));

        fx.Interaction.ShiftClick(InventoryFixture.Hotbar(1));
        var toBag = Assert.IsType<ItemMoveIntent>(fx.Sender.Intents[1]);
        Assert.Equal(ItemMoveTarget.AnyIn(InventoryFixture.BagId), toBag.To);
        Assert.Equal(InventoryFixture.Hotbar(1), toBag.From);
        Assert.Equal(InventoryFixture.Gem, toBag.ExpectedType);
    }

    [Fact]
    public void Discard_ResourceAsksConfirm_ThenDropIntent()
    {
        var fx = Rig();
        fx.Interaction.LeftClick(InventoryFixture.Bag(3));

        Assert.True(fx.Interaction.CanDiscard);
        Assert.True(fx.Interaction.Discard());
        Assert.True(fx.Interaction.AwaitingDiscardConfirmation);
        Assert.Empty(fx.Sender.Intents);

        fx.Interaction.ConfirmDiscard();

        var drop = Assert.IsType<ItemDropIntent>(Assert.Single(fx.Sender.Intents));
        Assert.Equal(InventoryFixture.Bag(3), drop.Slot);
        Assert.Equal<ushort>(12, drop.Count);
        Assert.Equal(InventoryFixture.Stone, drop.ExpectedType);
        Assert.Equal<ushort>(12, drop.ExpectedCount);
        Assert.False(fx.Interaction.AwaitingDiscardConfirmation);
        Assert.Null(fx.Interaction.Held);
    }

    [Fact]
    public void Discard_CancelKeepsTheStackInHand()
    {
        var fx = Rig();
        fx.Interaction.RightClick(InventoryFixture.Bag(3));
        fx.Interaction.Discard();

        fx.Interaction.CancelDiscard();

        Assert.False(fx.Interaction.AwaitingDiscardConfirmation);
        Assert.NotNull(fx.Interaction.Held);
        Assert.Empty(fx.Sender.Intents);
    }

    [Fact]
    public void Discard_Tool_Disabled()
    {
        var fx = Rig();
        fx.Interaction.LeftClick(InventoryFixture.Hotbar(0));

        Assert.NotNull(fx.Interaction.Held);
        Assert.False(fx.Interaction.CanDiscard);
        Assert.False(fx.Interaction.Discard());
        Assert.False(fx.Interaction.AwaitingDiscardConfirmation);
        fx.Interaction.ConfirmDiscard();
        Assert.Empty(fx.Sender.Intents);
    }

    [Fact]
    public void PendingSlot_BlocksSecondPick()
    {
        var fx = Rig();
        fx.Interaction.LeftClick(InventoryFixture.Bag(3));
        fx.Interaction.LeftClick(InventoryFixture.Hotbar(7));

        Assert.True(fx.Interaction.IsPending(InventoryFixture.Bag(3)));
        Assert.True(fx.Interaction.IsPending(InventoryFixture.Hotbar(7)));
        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.LeftClick(InventoryFixture.Bag(3)));
        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.RightClick(InventoryFixture.Hotbar(7)));
        Assert.Null(fx.Interaction.Held);
        Assert.Equal(InteractionOutcome.Picked, fx.Interaction.LeftClick(InventoryFixture.Bag(4))); // another slot is free to pick

        fx.Interaction.Cancel();
        fx.Interaction.OnIntentReply(Reply(fx, RejectReason.ContainerChanged));

        Assert.False(fx.Interaction.IsPending(InventoryFixture.Bag(3)));
        Assert.Equal(RejectReason.ContainerChanged, fx.Interaction.LastRefusal);
        Assert.Equal(InteractionOutcome.Picked, fx.Interaction.LeftClick(InventoryFixture.Bag(3)));
    }

    [Fact]
    public void Held_DropsWhenTheSourceChangesUnderneath()
    {
        var fx = Rig();
        fx.Interaction.LeftClick(InventoryFixture.Bag(3));

        fx.Delta(InventoryFixture.BagId, fx.Inventory.Bag!.Version + 1, (3, _stone, 4));

        Assert.Equal(InteractionOutcome.Cancelled, fx.Interaction.LeftClick(InventoryFixture.Bag(10)));
        Assert.Null(fx.Interaction.Held);
        Assert.Empty(fx.Sender.Intents);
    }

    [Fact]
    public void ClickOnTheSourceSlot_PutsTheStackBack()
    {
        var fx = Rig();
        fx.Interaction.LeftClick(InventoryFixture.Bag(3));

        Assert.Equal(InteractionOutcome.Cancelled, fx.Interaction.LeftClick(InventoryFixture.Bag(3)));

        Assert.Null(fx.Interaction.Held);
        Assert.Empty(fx.Sender.Intents);
    }

    [Fact]
    public void EmptySlot_CannotBePicked_And_NotInGameSendsNothing()
    {
        var fx = Rig();
        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.LeftClick(InventoryFixture.Bag(30)));

        fx.Interaction.LeftClick(InventoryFixture.Bag(3));
        fx.Sender.CanSend = false;
        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.LeftClick(InventoryFixture.Hotbar(7)));
        Assert.Empty(fx.Sender.Intents);
    }

    [Fact]
    public void Disconnect_ClearsHandAndPending()
    {
        var fx = Rig();
        fx.Interaction.LeftClick(InventoryFixture.Bag(3));
        fx.Interaction.LeftClick(InventoryFixture.Hotbar(7));
        fx.Interaction.LeftClick(InventoryFixture.Bag(4));

        fx.Interaction.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.Normal);

        Assert.Null(fx.Interaction.Held);
        Assert.False(fx.Interaction.IsPending(InventoryFixture.Bag(3)));
    }
}
