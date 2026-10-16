using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Server.Inventory;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Inventory;

/// <summary>
/// Subscriptions to the container of a chest block (K6-30 D20): access, 6 m to open, 8 m to keep, deltas for every subscriber, and the
/// states that end a subscription. The world is <see cref="WorldFactory.CreateWithFunctionalBlocks"/> with two players; the chest block
/// stands at <see cref="FunctionalBlocks.ChestCell"/> and the players are put at a measured distance of the eye from its centre.
/// </summary>
public sealed class ChestSubscriptionTests
{
    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
    private static readonly ItemTypeId _gem = WorldFactory.Item(SurvivalContentFactory.GemKey);

    private readonly InMemoryJournal _journal = new();
    private readonly ServerSimulation _sim;
    private readonly List<(PlayerId Player, Message Message)> _sent = [];
    private readonly InventoryService _service;
    private readonly PlayerId _owner = new(1);
    private readonly PlayerId _other = new(2);

    public ChestSubscriptionTests()
    {
        var world = WorldFactory.CreateWithFunctionalBlocks(players: 2);
        _sim = new ServerSimulation(
            world, new FakeTerrainService(), _journal, new GameRules(), NullLogger<ServerSimulation>.Instance, FunctionalBlocks.ContentFor(world),
            SurvivalContentFactory.CreateTest());
        _service = new InventoryService(
            () => _sim, (player, message) => _sent.Add((player, message)), () => false, NullLogger<InventoryService>.Instance);
        _service.OnPlayerReady(_owner);
        _service.OnPlayerReady(_other);
    }

    private ContainerId SetUpChest(ContainerAccess access = ContainerAccess.Shared, ushort size = 8)
    {
        var chest = _sim.State.ContainerIds.PeekNext();
        _sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(
                chest, ContainerKind.Chest, ContainerHost.ConstructBlock(FunctionalBlocks.Construct, FunctionalBlocks.ChestCell), _owner, access, size),
            new BlockEntitySetOp(FunctionalBlocks.Construct, FunctionalBlocks.ChestCell, FunctionalBlocks.ChestType, new ChestBlockState(chest)),
        ]));
        _sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(chest, 0), _stone, 40, 500, ItemSource.Admin));
        _sent.Clear();
        return chest;
    }

    /// <summary>Puts the eye of the player <paramref name="meters"/> above the centre of the chest block (the up axis there is the world Y within a hair).</summary>
    private void Stand(PlayerId player, float meters)
    {
        var centre = new Vector3(0.5f, 4000f + FunctionalBlocks.ChestCell.Y + 0.5f, 0.5f);
        var feet = centre + new Vector3(0f, meters - _sim.Rules.EyeHeight, 0f);
        Assert.True(_sim.State.Players.TryGet(player, out var state));
        Assert.True(_sim.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
    }

    private void ApplyItem(ContainerId chest, int slot, ItemTypeId type, ushort count) =>
        _sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(chest, (ushort)slot), type, count, 500, ItemSource.Admin));

    private List<Message> SentTo(PlayerId player) => _sent.Where(s => s.Player == player).Select(s => s.Message).ToList();

    [Fact]
    public void Subscribe_SharedChestWithin6m_SendsState()
    {
        var chest = SetUpChest();
        Stand(_other, 5.9f);

        Assert.True(_service.Handle(_other, new ContainerSubscribe(chest)));

        var state = Assert.IsType<ContainerState>(Assert.Single(SentTo(_other)));
        Assert.Equal((chest, RejectReason.None, ContainerKind.Chest, ContainerAccess.Shared), (state.Container, state.Reason, state.Kind, state.Access));
        Assert.Equal((8, 2U), (state.Size, state.Version));
        Assert.Equal(new SlotContent(0, new ItemStack(_stone, 40)), Assert.Single(state.Slots));
    }

    [Fact]
    public void Subscribe_Beyond6m_OutOfReach()
    {
        var chest = SetUpChest();
        Stand(_other, 6.5f);

        Assert.True(_service.Handle(_other, new ContainerSubscribe(chest)));
        ApplyItem(chest, 1, _gem, 1);

        Assert.Equal([ContainerState.Rejected(chest, RejectReason.OutOfReach)], SentTo(_other));
    }

    [Fact]
    public void Subscribe_PrivateChestOfOther_ContainerLocked()
    {
        var chest = SetUpChest(ContainerAccess.Private);
        Stand(_other, 3f);
        Stand(_owner, 3f);

        _service.Handle(_other, new ContainerSubscribe(chest));
        _service.Handle(_owner, new ContainerSubscribe(chest));

        Assert.Equal([ContainerState.Rejected(chest, RejectReason.ContainerLocked)], SentTo(_other));
        var state = Assert.IsType<ContainerState>(Assert.Single(SentTo(_owner)));
        Assert.Equal((RejectReason.None, ContainerAccess.Private), (state.Reason, state.Access));
    }

    [Fact]
    public void WalkAwayBeyond8m_ServerEndsSubscription()
    {
        var chest = SetUpChest();
        Stand(_other, 5f);
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sent.Clear();

        // Between 6 m and 8 m the subscription is kept (opening needs 6 m, keeping 8 m).
        Stand(_other, 7.5f);
        _service.CheckSubscriptions();
        ApplyItem(chest, 1, _gem, 1);
        Assert.Equal([typeof(ContainerDelta)], SentTo(_other).Select(m => m.GetType()));
        _sent.Clear();

        Stand(_other, 8.5f);
        _service.CheckSubscriptions();
        _service.CheckSubscriptions();
        ApplyItem(chest, 2, _gem, 1);

        Assert.Equal([ContainerState.Rejected(chest, RejectReason.OutOfReach)], SentTo(_other));
    }

    [Fact]
    public void Delta_SentToAllSubscribers()
    {
        var chest = SetUpChest();
        Stand(_owner, 4f);
        Stand(_other, 5f);
        _service.Handle(_owner, new ContainerSubscribe(chest));
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sent.Clear();

        ApplyItem(chest, 1, _gem, 2);

        foreach (var player in new[] { _owner, _other })
        {
            var delta = Assert.IsType<ContainerDelta>(Assert.Single(SentTo(player)));
            Assert.Equal((chest, 3U), (delta.Container, delta.Version));
            Assert.Equal(new SlotContent(1, new ItemStack(_gem, 2)), Assert.Single(delta.Changed));
        }
    }

    [Fact]
    public void ChestRemoved_SubscribersGetLocked()
    {
        var chest = SetUpChest();
        Stand(_owner, 4f);
        Stand(_other, 5f);
        _service.Handle(_owner, new ContainerSubscribe(chest));
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sim.ApplyServerOperation(new ItemDestroyOp(new SlotRef(chest, 0), _stone, 40, ItemSink.Admin));
        _sent.Clear();

        _sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerRemoveOp(chest),
            new BlockEntitySetOp(FunctionalBlocks.Construct, FunctionalBlocks.ChestCell, FunctionalBlocks.ChestType, null),
            new BlockClearOp(FunctionalBlocks.Construct, FunctionalBlocks.ChestCell),
        ]));

        Assert.Equal([ContainerState.Rejected(chest, RejectReason.ContainerLocked)], SentTo(_owner));
        Assert.Equal([ContainerState.Rejected(chest, RejectReason.ContainerLocked)], SentTo(_other));
    }

    [Fact]
    public void SwitchToPrivate_NonOwnerSubscriptionsEnded()
    {
        var chest = SetUpChest();
        Stand(_owner, 4f);
        Stand(_other, 5f);
        _service.Handle(_owner, new ContainerSubscribe(chest));
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sent.Clear();

        _sim.ApplyServerOperation(new ContainerConfigureOp(chest, 8, ContainerAccess.Private));

        var ownerState = Assert.IsType<ContainerState>(Assert.Single(SentTo(_owner)));
        Assert.Equal((RejectReason.None, ContainerAccess.Private), (ownerState.Reason, ownerState.Access));
        Assert.Equal([ContainerState.Rejected(chest, RejectReason.ContainerLocked)], SentTo(_other));

        _sent.Clear();
        ApplyItem(chest, 1, _gem, 1);
        Assert.Equal([typeof(ContainerDelta)], SentTo(_owner).Select(m => m.GetType()));
        Assert.Empty(SentTo(_other));
    }

    [Fact]
    public void Configure_SubscribersGetFullState()
    {
        var chest = SetUpChest();
        Stand(_owner, 4f);
        Stand(_other, 5f);
        _service.Handle(_owner, new ContainerSubscribe(chest));
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sent.Clear();

        _sim.ApplyServerOperation(new ContainerConfigureOp(chest, 12, ContainerAccess.Shared));

        foreach (var player in new[] { _owner, _other })
        {
            var state = Assert.IsType<ContainerState>(Assert.Single(SentTo(player)));
            Assert.Equal((chest, RejectReason.None, (ushort)12, 3U), (state.Container, state.Reason, state.Size, state.Version));
            Assert.Equal(new SlotContent(0, new ItemStack(_stone, 40)), Assert.Single(state.Slots));
        }
    }

    [Fact]
    public void ConfigureWithTransferIntoNewSlot_SubscribersGetFullState()
    {
        var chest = SetUpChest();
        Stand(_other, 5f);
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sent.Clear();

        _sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerConfigureOp(chest, 12, ContainerAccess.Shared),
            new ItemTransferOp(new SlotRef(chest, 0), new SlotRef(chest, 10), _stone, 5, 500),
        ]));

        var state = Assert.IsType<ContainerState>(Assert.Single(SentTo(_other)));
        Assert.Equal((chest, RejectReason.None, (ushort)12), (state.Container, state.Reason, state.Size));
        Assert.Contains(new SlotContent(10, new ItemStack(_stone, 5)), state.Slots);
    }

    [Fact]
    public void Disconnect_SubscriptionDropped()
    {
        var chest = SetUpChest();
        Stand(_other, 5f);
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sent.Clear();
        ApplyItem(chest, 3, _gem, 1);
        Assert.Equal([typeof(ContainerDelta)], SentTo(_other).Select(m => m.GetType()));
        _sent.Clear();

        _service.OnPlayerLeaving(_other, DisconnectCode.Normal);
        ApplyItem(chest, 1, _gem, 1);
        _service.CheckSubscriptions();

        Assert.Empty(SentTo(_other));
    }

    [Fact]
    public void UnsubscribeChest_StopsDeltas()
    {
        var chest = SetUpChest();
        Stand(_owner, 4f);
        Stand(_other, 5f);
        _service.Handle(_owner, new ContainerSubscribe(chest));
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sent.Clear();

        ApplyItem(chest, 3, _gem, 1);
        Assert.Equal([typeof(ContainerDelta)], SentTo(_other).Select(m => m.GetType()));
        _sent.Clear();

        Assert.True(_service.Handle(_other, new ContainerUnsubscribe(chest)));
        ApplyItem(chest, 1, _gem, 1);

        Assert.Empty(SentTo(_other));
        Assert.Equal([typeof(ContainerDelta)], SentTo(_owner).Select(m => m.GetType()));
    }
}
