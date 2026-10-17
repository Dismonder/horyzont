using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Server.Inventory;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Inventory;

/// <summary>
/// K6-35 B4, subscriptions to a tombstone (D13, D19): only the owner opens it, within 6 m, and keeps it within 8 m; nobody else gets its contents, its changes or a hint of
/// them; a player at zero health opens nothing, loses what is open, and the server can end every subscription of a player before the operation of a death.
/// </summary>
public sealed class DeathAccessSubscriptionTests
{
    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
    private static readonly ItemTypeId _gem = WorldFactory.Item(SurvivalContentFactory.GemKey);

    private readonly ServerSimulation _sim;
    private readonly List<(PlayerId Player, Message Message)> _sent = [];
    private readonly InventoryService _service;
    private readonly PlayerId _owner = new(1);
    private readonly PlayerId _other = new(2);

    public DeathAccessSubscriptionTests()
    {
        var world = WorldFactory.CreateWithFunctionalBlocks(players: 2);
        _sim = new ServerSimulation(
            world, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance, FunctionalBlocks.ContentFor(world),
            SurvivalContentFactory.CreateTest());
        _service = new InventoryService(
            () => _sim, (player, message) => _sent.Add((player, message)), () => false, NullLogger<InventoryService>.Instance);
        _service.OnPlayerReady(_owner);
        _service.OnPlayerReady(_other);
    }

    private static Vector3 TombstonePosition => new(0.5f, 4000.5f, 0.5f);

    /// <summary>A tombstone of the owner with a stone and a gem in two of its three slots, beside the point where <see cref="Stand"/> measures.</summary>
    private (ContainerId Container, EntityId Entity) SetUpTombstone()
    {
        var entity = _sim.State.EntityIds.PeekNext();
        var container = _sim.State.ContainerIds.PeekNext();
        _sim.ApplyServerOperation(new CompoundOp(
        [
            new PersistentEntityCreateOp(entity, PersistentEntityKind.Tombstone, FrameRef.Of(WorldFactory.HomePlanet), 0, TombstonePosition, Quaternion.Identity, _owner),
            new ContainerCreateOp(container, ContainerKind.Tombstone, ContainerHost.PersistentEntity(entity), _owner, ContainerAccess.Private, 3),
        ]));
        _sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(container, 0), _stone, 40, 500, ItemSource.Admin));
        _sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(container, 1), _gem, 3, 500, ItemSource.Admin));
        _sent.Clear();
        return (container, entity);
    }

    /// <summary>Puts the eye of the player <paramref name="meters"/> above the tombstone.</summary>
    private void Stand(PlayerId player, float meters)
    {
        var feet = TombstonePosition + new Vector3(0f, meters - _sim.Rules.EyeHeight, 0f);
        Assert.True(_sim.State.Players.TryGet(player, out var state));
        Assert.True(_sim.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
    }

    private List<Message> SentTo(PlayerId player) => _sent.Where(s => s.Player == player).Select(s => s.Message).ToList();

    private void Kill(PlayerId player) => Assert.True(_sim.UpdatePlayerVitals(player, new PlayerVitals(0f, 50f, default, false)));

    [Fact]
    public void OtherPlayer_CannotSubscribeOrTakeItems()
    {
        var (tombstone, _) = SetUpTombstone();
        Stand(_other, 1f);
        var hash = Horyzont.Core.Hashing.StateHasher.Hash(_sim.State);

        // Right next to it: the answer is the lock, with no slot, no kind and no size; later changes do not reach the other player either.
        Assert.True(_service.Handle(_other, new ContainerSubscribe(tombstone)));
        _sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(tombstone, 2), _gem, 1, 500, ItemSource.Admin));

        Assert.Equal([ContainerState.Rejected(tombstone, RejectReason.ContainerLocked)], SentTo(_other));
        Assert.Empty(SentTo(_owner));
        Assert.NotEqual(hash, Horyzont.Core.Hashing.StateHasher.Hash(_sim.State));

        // The take is refused too, and the world stays as it was.
        var before = _sim.State.Containers.TryGet(tombstone, out var container) ? container.Slots.ToArray() : [];
        Assert.Equal(RejectReason.ContainerLocked, _sim.Submit(_other, new TakeAllIntent(tombstone)).Reason);
        Assert.Equal(before, container!.Slots.ToArray());
    }

    [Fact]
    public void Owner_Within6m_GetsTheState_BeyondItOutOfReach()
    {
        var (tombstone, _) = SetUpTombstone();

        Stand(_owner, 6.5f);
        _service.Handle(_owner, new ContainerSubscribe(tombstone));
        Assert.Equal([ContainerState.Rejected(tombstone, RejectReason.OutOfReach)], SentTo(_owner));

        _sent.Clear();
        Stand(_owner, 5.9f);
        _service.Handle(_owner, new ContainerSubscribe(tombstone));
        var state = Assert.IsType<ContainerState>(Assert.Single(SentTo(_owner)));
        Assert.Equal((tombstone, RejectReason.None, ContainerKind.Tombstone, ContainerAccess.Private, 3), (state.Container, state.Reason, state.Kind, state.Access, state.Size));
        Assert.Equal(
            [new SlotContent(0, new ItemStack(_stone, 40)), new SlotContent(1, new ItemStack(_gem, 3))], state.Slots);
    }

    [Fact]
    public void Owner_Subscribed_GetsDeltas_OthersNone_AndTheEndWhenItIsGone()
    {
        var (tombstone, entity) = SetUpTombstone();
        Stand(_owner, 2f);
        Stand(_other, 2f);
        _service.Handle(_owner, new ContainerSubscribe(tombstone));
        _service.Handle(_other, new ContainerSubscribe(tombstone));
        _sent.Clear();

        // A change reaches the owner as a delta and nobody else; before the subscription the owner got nothing unasked either.
        _sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(tombstone, 2), _gem, 1, 500, ItemSource.Admin));
        var delta = Assert.IsType<ContainerDelta>(Assert.Single(SentTo(_owner)));
        Assert.Equal(tombstone, delta.Container);
        Assert.Empty(SentTo(_other));
        _sent.Clear();

        // The last item leaves with the container and the entity: the owner's panel is closed by the refusal, nothing stays open on a container that does not exist.
        _sim.ApplyServerOperation(new CompoundOp(
        [
            new ItemDestroyOp(new SlotRef(tombstone, 0), _stone, 40, ItemSink.Admin), new ItemDestroyOp(new SlotRef(tombstone, 1), _gem, 3, ItemSink.Admin),
            new ItemDestroyOp(new SlotRef(tombstone, 2), _gem, 1, ItemSink.Admin), new ContainerRemoveOp(tombstone), new PersistentEntityRemoveOp(entity),
        ]));
        Assert.Equal([ContainerState.Rejected(tombstone, RejectReason.ContainerLocked)], SentTo(_owner));
        Assert.Empty(SentTo(_other));
    }

    [Fact]
    public void WalkAwayBeyond8m_ServerEndsSubscription()
    {
        var (tombstone, _) = SetUpTombstone();
        Stand(_owner, 5f);
        _service.Handle(_owner, new ContainerSubscribe(tombstone));
        _sent.Clear();

        // Between 6 m and 8 m the subscription is kept (opening needs 6 m, keeping 8 m).
        Stand(_owner, 7.5f);
        _service.CheckSubscriptions();
        Assert.Empty(SentTo(_owner));

        Stand(_owner, 8.5f);
        _service.CheckSubscriptions();
        Assert.Equal([ContainerState.Rejected(tombstone, RejectReason.OutOfReach)], SentTo(_owner));
    }

    [Fact]
    public void PendingRespawn_RejectsSubscriptionsWithInTransition()
    {
        var (tombstone, _) = SetUpTombstone();
        Stand(_owner, 2f);
        Kill(_owner);

        // Zero health: the answer is InTransition, whatever the container and the reach; the same for a container the player owns.
        _service.Handle(_owner, new ContainerSubscribe(tombstone));
        Assert.True(_sim.State.Containers.TryGetOfPlayer(_owner, ContainerKind.PlayerBag, out var bag));
        _service.Handle(_owner, new ContainerSubscribe(bag.Id));
        Assert.Equal([ContainerState.Rejected(tombstone, RejectReason.InTransition), ContainerState.Rejected(bag.Id, RejectReason.InTransition)], SentTo(_owner));
    }

    [Fact]
    public void PendingRespawn_EndsAnOpenSubscription()
    {
        var (tombstone, _) = SetUpTombstone();
        Stand(_owner, 2f);
        _service.Handle(_owner, new ContainerSubscribe(tombstone));
        _sent.Clear();

        Kill(_owner);
        _service.CheckSubscriptions();

        Assert.Equal([ContainerState.Rejected(tombstone, RejectReason.InTransition)], SentTo(_owner));
    }

    [Fact]
    public void EndSubscriptions_EndsEveryOneOfThePlayerOnly()
    {
        var (tombstone, _) = SetUpTombstone();
        var chest = _sim.State.ContainerIds.PeekNext();
        _sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(
                chest, ContainerKind.Chest, ContainerHost.ConstructBlock(FunctionalBlocks.Construct, FunctionalBlocks.ChestCell), _owner, ContainerAccess.Shared, 4),
            new BlockEntitySetOp(FunctionalBlocks.Construct, FunctionalBlocks.ChestCell, FunctionalBlocks.ChestType, new Horyzont.Core.BlockEntities.ChestBlockState(chest)),
        ]));
        var centre = new Vector3(0.5f, 4000f + FunctionalBlocks.ChestCell.Y + 0.5f, 0.5f);
        Assert.True(_sim.State.Players.TryGet(_owner, out var ownerState));
        Assert.True(_sim.State.Players.TryGet(_other, out var otherState));
        Assert.True(_sim.UpdatePlayerKinematics(_owner, ownerState.Frame, ownerState.ParentSeq, TombstonePosition, Quaternion.Identity, 0f));
        Assert.True(_sim.UpdatePlayerKinematics(_other, otherState.Frame, otherState.ParentSeq, centre, Quaternion.Identity, 0f));
        _service.Handle(_owner, new ContainerSubscribe(tombstone));
        _service.Handle(_owner, new ContainerSubscribe(chest));
        _service.Handle(_other, new ContainerSubscribe(chest));
        _sent.Clear();

        Assert.Equal(2, _service.EndSubscriptions(_owner, RejectReason.InTransition));

        Assert.Equal(
            [ContainerState.Rejected(tombstone, RejectReason.InTransition), ContainerState.Rejected(chest, RejectReason.InTransition)],
            SentTo(_owner).OrderBy(m => ((ContainerState)m).Container.Value).ToList());
        Assert.Empty(SentTo(_other));
        Assert.Equal(0, _service.EndSubscriptions(_owner, RejectReason.InTransition));

        // The other player still has the chest: a change reaches that player and no longer the owner.
        _sent.Clear();
        _sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(chest, 0), _gem, 1, 500, ItemSource.Admin));
        Assert.Empty(SentTo(_owner));
        Assert.IsType<ContainerDelta>(Assert.Single(SentTo(_other)));
    }
}
