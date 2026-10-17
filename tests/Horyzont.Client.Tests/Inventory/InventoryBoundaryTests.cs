using Horyzont.Client.Inventory;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Tests.Inventory;

public sealed class InventoryBoundaryTests
{
    [Theory]
    [InlineData(1, 40)]
    [InlineData(2, 10)]
    [InlineData(1, ushort.MaxValue)]
    [InlineData(99, 0)]
    public void Click_NonexistentSourceSlot_IgnoresAllPickupActions(int container, int slot)
    {
        var fx = new InventoryFixture();
        var source = new SlotRef(new((ulong)container), (ushort)slot);

        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.LeftClick(source));
        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.RightClick(source));
        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.ShiftClick(source));

        Assert.Null(fx.Interaction.Held);
        Assert.False(fx.Interaction.HasPending);
        Assert.Empty(fx.Sender.Intents);
    }

    [Theory]
    [InlineData(1, 40)]
    [InlineData(2, 10)]
    [InlineData(2, ushort.MaxValue)]
    [InlineData(99, 0)]
    public void Click_NonexistentTargetSlot_KeepsHandAndSendsNothing(int container, int slot)
    {
        var fx = new InventoryFixture();
        fx.Put(InventoryFixture.Bag(3), InventoryFixture.Stone, 12);
        fx.Interaction.LeftClick(InventoryFixture.Bag(3));
        var held = fx.Interaction.Held;
        var target = new SlotRef(new((ulong)container), (ushort)slot);

        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.LeftClick(target));
        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.RightClick(target));

        Assert.Equal(held, fx.Interaction.Held);
        Assert.False(fx.Interaction.HasPending);
        Assert.Empty(fx.Sender.Intents);
    }

    [Fact]
    public void LeftClick_FullTargetStack_KeepsHandAndDoesNotPredictTransfer()
    {
        var fx = new InventoryFixture();
        fx.Put(InventoryFixture.Bag(3), InventoryFixture.Stone, 12);
        fx.Put(InventoryFixture.Hotbar(7), InventoryFixture.Stone, 500);
        fx.Interaction.LeftClick(InventoryFixture.Bag(3));

        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.LeftClick(InventoryFixture.Hotbar(7)));

        Assert.NotNull(fx.Interaction.Held);
        Assert.Equal(12, fx.Inventory.Bag!.Get(3).Count);
        Assert.Equal(500, fx.Inventory.Hotbar!.Get(7).Count);
        Assert.Empty(fx.Sender.Intents);
    }

    [Fact]
    public void OnIntentReply_ContainerChanged_ReleasesPendingSlotsWithoutChangingMirror()
    {
        var fx = new InventoryFixture();
        var source = InventoryFixture.Hotbar(1);
        var target = InventoryFixture.Bag(0);
        fx.Interaction.LeftClick(source);
        fx.Interaction.LeftClick(target);

        fx.Interaction.OnIntentReply(new IntentReply(fx.Sender.LastSeq, RejectReason.ContainerChanged, default));

        Assert.Equal(RejectReason.ContainerChanged, fx.Interaction.LastRefusal);
        Assert.False(fx.Interaction.IsPending(source));
        Assert.False(fx.Interaction.IsPending(target));
        Assert.Equal(3, fx.Inventory.Hotbar!.Get(1).Count);
        Assert.True(fx.Inventory.Bag!.Get(0).IsEmpty);
    }

    [Fact]
    public void ValidateHeld_SourceContainerReplaced_CancelsHandAndDiscardConfirmation()
    {
        var fx = new InventoryFixture();
        fx.Interaction.LeftClick(InventoryFixture.Hotbar(1));
        Assert.True(fx.Interaction.Discard());

        fx.State(InventoryFixture.HotbarId, ContainerKind.Hotbar, 10, 8, (1, InventoryFixture.Stone, 3));

        Assert.True(fx.Interaction.ValidateHeld());
        Assert.Null(fx.Interaction.Held);
        Assert.False(fx.Interaction.AwaitingDiscardConfirmation);
        fx.Interaction.ConfirmDiscard();
        Assert.Empty(fx.Sender.Intents);
    }

    [Fact]
    public void OnDisconnected_NewHostContainersArrive_DropsOldReferencesAndPendingMoves()
    {
        var fx = new InventoryFixture();
        fx.Interaction.LeftClick(InventoryFixture.Hotbar(1));
        fx.Interaction.LeftClick(InventoryFixture.Bag(0));
        fx.Interaction.LeftClick(InventoryFixture.Hotbar(0));

        fx.Inventory.OnDisconnected(DisconnectCode.Normal);
        fx.Interaction.OnDisconnected(DisconnectCode.Normal);
        fx.State(new(11), ContainerKind.PlayerBag, 40, 1);
        fx.State(new(12), ContainerKind.Hotbar, 10, 1, (0, InventoryFixture.Stone, 2));

        Assert.False(fx.Inventory.TryGetContainer(InventoryFixture.HotbarId, out _));
        Assert.False(fx.Inventory.TryGetContainer(InventoryFixture.BagId, out _));
        Assert.Equal(12UL, fx.Inventory.Hotbar!.Id.Value);
        Assert.Null(fx.Interaction.Held);
        Assert.False(fx.Interaction.HasPending);
        Assert.Equal(InteractionOutcome.Ignored, fx.Interaction.ShiftClick(InventoryFixture.Hotbar(0)));
        Assert.Single(fx.Sender.Intents);
    }
}
