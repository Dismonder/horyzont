using Horyzont.Client.Inventory;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Inventory;

public sealed class ChestSessionTests
{
    private static readonly ContainerId _chestId = new(10);
    private static readonly EntityId _construct = new(100);
    private static readonly CellPos _cell = new(0, 1, 0);

    private sealed class Rig
    {
        public Rig()
        {
            Fixture = new InventoryFixture();
            Chests = new ChestSession(Fixture.Inventory, Fixture.Sender, Sent.Add, NullLogger<ChestSession>.Instance);
            Interaction = new InventoryInteraction(Fixture.Inventory, Fixture.Sender, NullLogger<InventoryInteraction>.Instance, Chests);
        }

        public InventoryFixture Fixture { get; }

        public ChestSession Chests { get; }

        public InventoryInteraction Interaction { get; }

        public List<Message> Sent { get; } = [];

        public int Opened { get; private set; }

        public int Closed { get; private set; }

        public Rig Watch()
        {
            Chests.Opened += () => Opened++;
            Chests.Closed += () => Closed++;
            return this;
        }

        public void ChestState(ContainerAccess access = ContainerAccess.Shared, params (int Slot, ItemTypeId Type, int Count)[] slots) =>
            Fixture.Inventory.OnMessage(
                new ContainerState(_chestId, RejectReason.None, ContainerKind.Chest, access, 20, 1,
                    [.. slots.Select(s => new SlotContent((ushort)s.Slot, new ItemStack(s.Type, (ushort)s.Count)))]));

        public void OpenAndArrive(ContainerAccess access = ContainerAccess.Shared, params (int Slot, ItemTypeId Type, int Count)[] slots)
        {
            Chests.Open(_chestId, _construct, _cell);
            ChestState(access, slots);
        }
    }

    [Fact]
    public void Open_StateStored_Opened()
    {
        var rig = new Rig().Watch();

        rig.Chests.Open(_chestId, _construct, _cell);

        Assert.Equal(new ContainerSubscribe(_chestId), Assert.Single(rig.Sent));
        Assert.True(rig.Chests.IsOpening);
        Assert.False(rig.Chests.IsOpen);
        Assert.Equal(0, rig.Opened);

        rig.ChestState(ContainerAccess.Shared, (0, InventoryFixture.Stone, 5));

        Assert.True(rig.Chests.IsOpen);
        Assert.False(rig.Chests.IsOpening);
        Assert.Equal(1, rig.Opened);
        Assert.Equal(_chestId, rig.Chests.Container);
        Assert.Equal(_construct, rig.Chests.Construct);
        Assert.Equal(_cell, rig.Chests.Cell);
        Assert.Equal(new ItemStack(InventoryFixture.Stone, 5), rig.Chests.OpenContainer!.Get(0));
    }

    [Fact]
    public void StateOfAnotherContainer_IsNotTheOpenChest()
    {
        var rig = new Rig().Watch();
        rig.Chests.Open(_chestId, _construct, _cell);

        rig.Fixture.Inventory.OnMessage(
            new ContainerState(new ContainerId(11), RejectReason.None, ContainerKind.Chest, ContainerAccess.Shared, 20, 1, []));

        Assert.True(rig.Chests.IsOpening);
        Assert.Equal(0, rig.Opened);
    }

    [Fact]
    public void RejectedLocked_PromptLocked()
    {
        var rig = new Rig().Watch();
        rig.Chests.Open(_chestId, _construct, _cell);

        rig.Fixture.Inventory.OnMessage(ContainerState.Rejected(_chestId, RejectReason.ContainerLocked));

        Assert.False(rig.Chests.IsOpening);
        Assert.False(rig.Chests.IsOpen);
        Assert.True(rig.Chests.IsLocked(_chestId));
        Assert.False(rig.Chests.IsLocked(new ContainerId(11)));
        Assert.Equal(RejectReason.ContainerLocked, rig.Chests.LastRefusal);
        Assert.Equal(0, rig.Opened);
    }

    [Fact]
    public void OpenAgain_AfterLockedAndThenGranted_ClearsTheLock()
    {
        var rig = new Rig();
        rig.Chests.Open(_chestId, _construct, _cell);
        rig.Fixture.Inventory.OnMessage(ContainerState.Rejected(_chestId, RejectReason.ContainerLocked));

        rig.OpenAndArrive();

        Assert.True(rig.Chests.IsOpen);
        Assert.False(rig.Chests.IsLocked(_chestId));
        Assert.Equal(RejectReason.None, rig.Chests.LastRefusal);
    }

    [Fact]
    public void ServerOutOfReach_ClosesPanel()
    {
        var rig = new Rig().Watch();
        rig.OpenAndArrive();
        rig.Sent.Clear();

        rig.Fixture.Inventory.OnMessage(ContainerState.Rejected(_chestId, RejectReason.OutOfReach));

        Assert.False(rig.Chests.IsOpen);
        Assert.Equal(1, rig.Closed);
        Assert.Equal(RejectReason.OutOfReach, rig.Chests.LastRefusal);
        Assert.Empty(rig.Sent); // the server ended the subscription itself
        Assert.False(rig.Fixture.Inventory.TryGetContainer(_chestId, out _));
        Assert.False(rig.Chests.IsLocked(_chestId));
    }

    [Fact]
    public void ServerEndsSubscriptionOfAChestThatTurnedPrivate_ClosesAndRemembersLocked()
    {
        var rig = new Rig().Watch();
        rig.OpenAndArrive();

        rig.Fixture.Inventory.OnMessage(ContainerState.Rejected(_chestId, RejectReason.ContainerLocked));

        Assert.False(rig.Chests.IsOpen);
        Assert.Equal(1, rig.Closed);
        Assert.True(rig.Chests.IsLocked(_chestId));
    }

    [Fact]
    public void Close_SendsUnsubscribe()
    {
        var rig = new Rig().Watch();
        rig.OpenAndArrive(ContainerAccess.Shared, (0, InventoryFixture.Stone, 5));
        rig.Sent.Clear();

        rig.Chests.Close();

        Assert.Equal(new ContainerUnsubscribe(_chestId), Assert.Single(rig.Sent));
        Assert.False(rig.Chests.IsOpen);
        Assert.Equal(1, rig.Closed);
        Assert.False(rig.Fixture.Inventory.TryGetContainer(_chestId, out _));

        rig.Chests.Close();
        Assert.Single(rig.Sent); // closing twice sends nothing more
        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void Open_AnotherChestWhileOpen_UnsubscribesFromTheFirst()
    {
        var rig = new Rig();
        rig.OpenAndArrive();
        rig.Sent.Clear();

        rig.Chests.Open(new ContainerId(11), new EntityId(101), new CellPos(1, 1, 0));

        Assert.Equal([new ContainerUnsubscribe(_chestId), new ContainerSubscribe(new ContainerId(11))], rig.Sent);
        Assert.False(rig.Chests.IsOpen);
        Assert.True(rig.Chests.IsOpening);
    }

    [Fact]
    public void Disconnect_ForgetsTheChest()
    {
        var rig = new Rig().Watch();
        rig.OpenAndArrive();

        rig.Chests.OnDisconnected(default);

        Assert.False(rig.Chests.IsOpen);
        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void ChestDelta_RaisesNoOwnedCountChange()
    {
        var rig = new Rig();
        rig.OpenAndArrive();
        var changes = new List<(ItemTypeId Type, int Delta)>();
        rig.Fixture.Inventory.OwnedCountChanged += (type, delta) => changes.Add((type, delta));

        rig.Fixture.Delta(_chestId, 2, (0, InventoryFixture.Gem, 4));

        Assert.Empty(changes); // items in a chest are not carried by the player: no pickup toast
        Assert.Equal(new ItemStack(InventoryFixture.Gem, 4), rig.Chests.OpenContainer!.Get(0));
    }

    [Fact]
    public void MoveBagToChest_IntentCarriesExpectedSource()
    {
        var rig = new Rig();
        rig.Fixture.Put(InventoryFixture.Bag(3), InventoryFixture.Stone, 7);
        rig.OpenAndArrive();

        Assert.Equal(InteractionOutcome.Picked, rig.Interaction.LeftClick(InventoryFixture.Bag(3)));
        var target = new SlotRef(_chestId, 2);
        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.LeftClick(target));

        var intent = Assert.IsType<ItemMoveIntent>(Assert.Single(rig.Fixture.Sender.Intents));
        Assert.Equal(new ItemMoveIntent(InventoryFixture.Bag(3), ItemMoveTarget.ToSlot(target), 7, InventoryFixture.Stone, 7), intent);
        Assert.True(rig.Interaction.IsPending(target));
    }

    [Fact]
    public void MoveChestToBag_PicksFromTheChestSlot()
    {
        var rig = new Rig();
        rig.OpenAndArrive(ContainerAccess.Shared, (4, InventoryFixture.Soil, 9));

        Assert.Equal(InteractionOutcome.Picked, rig.Interaction.RightClick(new SlotRef(_chestId, 4)));
        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.LeftClick(InventoryFixture.Bag(0)));

        var intent = Assert.IsType<ItemMoveIntent>(Assert.Single(rig.Fixture.Sender.Intents));
        Assert.Equal(new ItemMoveIntent(new SlotRef(_chestId, 4), ItemMoveTarget.ToSlot(InventoryFixture.Bag(0)), 5, InventoryFixture.Soil, 9), intent);
    }

    [Fact]
    public void HeldFromChest_ChestClosed_HandIsCleared()
    {
        var rig = new Rig();
        rig.OpenAndArrive(ContainerAccess.Shared, (4, InventoryFixture.Soil, 9));
        rig.Interaction.LeftClick(new SlotRef(_chestId, 4));
        Assert.NotNull(rig.Interaction.Held);

        rig.Chests.Close();

        Assert.True(rig.Interaction.ValidateHeld());
        Assert.Null(rig.Interaction.Held);
    }

    [Fact]
    public void ShiftClickInChest_MovesAnyToBag()
    {
        var rig = new Rig();
        rig.OpenAndArrive(ContainerAccess.Shared, (4, InventoryFixture.Soil, 9));

        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.ShiftClick(new SlotRef(_chestId, 4)));

        var intent = Assert.IsType<ItemMoveIntent>(Assert.Single(rig.Fixture.Sender.Intents));
        Assert.Equal(new ItemMoveIntent(new SlotRef(_chestId, 4), ItemMoveTarget.AnyIn(InventoryFixture.BagId), 9, InventoryFixture.Soil, 9), intent);
    }

    [Fact]
    public void ShiftClickInBag_WithChestOpen_MovesAnyToChest()
    {
        var rig = new Rig();
        rig.Fixture.Put(InventoryFixture.Bag(3), InventoryFixture.Stone, 7);
        rig.OpenAndArrive();

        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.ShiftClick(InventoryFixture.Bag(3)));

        var intent = Assert.IsType<ItemMoveIntent>(Assert.Single(rig.Fixture.Sender.Intents));
        Assert.Equal(ItemMoveTarget.AnyIn(_chestId), intent.To);
    }

    [Fact]
    public void ShiftClickInBag_WithoutChest_StillMovesToTheHotbar()
    {
        var rig = new Rig();
        rig.Fixture.Put(InventoryFixture.Bag(3), InventoryFixture.Stone, 7);

        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.ShiftClick(InventoryFixture.Bag(3)));

        var intent = Assert.IsType<ItemMoveIntent>(Assert.Single(rig.Fixture.Sender.Intents));
        Assert.Equal(ItemMoveTarget.AnyIn(InventoryFixture.HotbarId), intent.To);
    }

    [Fact]
    public void QuickStackButton_SendsIntent()
    {
        var rig = new Rig();

        Assert.True(rig.Chests.QuickStack());

        Assert.IsType<QuickStackIntent>(Assert.Single(rig.Fixture.Sender.Intents));

        rig.Fixture.Sender.CanSend = false;
        Assert.False(rig.Chests.QuickStack());
        Assert.Single(rig.Fixture.Sender.Intents);
    }

    [Fact]
    public void QuickStackRefused_ShowsTheReason()
    {
        var rig = new Rig();
        rig.Chests.QuickStack();

        rig.Chests.OnIntentReply(new IntentReply(rig.Fixture.Sender.LastSeq, RejectReason.NotEnoughItems, default));

        Assert.Equal(RejectReason.NotEnoughItems, rig.Chests.LastRefusal);
    }

    [Fact]
    public void TakeAllButton_SendsIntent()
    {
        var rig = new Rig();
        Assert.False(rig.Chests.TakeAll()); // no chest open
        Assert.Empty(rig.Fixture.Sender.Intents);

        rig.OpenAndArrive(ContainerAccess.Shared, (0, InventoryFixture.Stone, 5));

        Assert.True(rig.Chests.TakeAll());
        Assert.Equal(new TakeAllIntent(_chestId), Assert.Single(rig.Fixture.Sender.Intents));
    }

    [Fact]
    public void AccessToggle_OwnerOnly()
    {
        var rig = new Rig();
        rig.OpenAndArrive(ContainerAccess.Private);
        Assert.True(rig.Chests.CanConfigureAccess); // a private chest opened at all can only be the own one

        Assert.True(rig.Chests.ToggleAccess());
        var intent = Assert.IsType<BlockConfigIntent>(Assert.Single(rig.Fixture.Sender.Intents));
        Assert.Equal(new BlockConfigIntent(_construct, _cell, new ContainerAccessConfig(ContainerAccess.Shared)), intent);

        // The server says the chest is not the player's: the switch goes away and nothing more is sent.
        rig.Chests.OnIntentReply(new IntentReply(rig.Fixture.Sender.LastSeq, RejectReason.NotOwner, default));
        Assert.Equal(RejectReason.NotOwner, rig.Chests.LastRefusal);
        Assert.False(rig.Chests.CanConfigureAccess);
        Assert.False(rig.Chests.ToggleAccess());
        Assert.Single(rig.Fixture.Sender.Intents);
    }

    [Fact]
    public void AccessToggle_SharedChest_AsksForPrivate()
    {
        var rig = new Rig();
        rig.OpenAndArrive(ContainerAccess.Shared);

        Assert.True(rig.Chests.CanConfigureAccess); // the owner of a shared chest is not replicated: offered until the server refuses

        Assert.True(rig.Chests.ToggleAccess());
        Assert.Equal(
            new BlockConfigIntent(_construct, _cell, new ContainerAccessConfig(ContainerAccess.Private)),
            Assert.IsType<BlockConfigIntent>(Assert.Single(rig.Fixture.Sender.Intents)));
    }

    [Fact]
    public void AcceptedReply_ClearsAnEarlierRefusal()
    {
        var rig = new Rig();
        rig.Chests.QuickStack();
        rig.Chests.OnIntentReply(new IntentReply(rig.Fixture.Sender.LastSeq, RejectReason.NotEnoughItems, default));
        rig.Chests.QuickStack();

        rig.Chests.OnIntentReply(new IntentReply(rig.Fixture.Sender.LastSeq, RejectReason.None, default));

        Assert.Equal(RejectReason.None, rig.Chests.LastRefusal);
    }

    [Fact]
    public void ReplyToAnIntentOfAnotherComponent_IsIgnored()
    {
        var rig = new Rig();

        rig.Chests.OnIntentReply(new IntentReply(9999, RejectReason.NotOwner, default));

        Assert.Equal(RejectReason.None, rig.Chests.LastRefusal);
    }
}
