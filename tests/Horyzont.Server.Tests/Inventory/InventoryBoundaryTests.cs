using System.Numerics;

using Horyzont.Client.Inventory;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Server.Inventory;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Inventory;

public sealed class InventoryBoundaryTests
{
    private readonly InMemoryJournal _journal = new();
    private readonly FakeTerrainService _terrain = new();
    private readonly List<Message> _sent = [];
    private readonly ServerSimulation _sim;
    private readonly InventoryService _service;
    private readonly PlayerId _player;

    public InventoryBoundaryTests()
    {
        _sim = NewSimulation(_terrain, _journal);
        _service = new InventoryService(() => _sim, (_, message) => _sent.Add(message), () => false,
            NullLogger<InventoryService>.Instance);
        _player = Join(1);
        _service.OnPlayerReady(_player);
        _sent.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Submit_ForeignContainerAtEitherEnd_RejectsWithoutChanges(bool foreignSource)
    {
        var other = Join(2);
        _service.OnPlayerReady(other);
        var own = GetContainer(_player, ContainerKind.Hotbar);
        var foreign = GetContainer(other, ContainerKind.Hotbar);
        var from = foreignSource ? foreign : own;
        var to = foreignSource ? own : foreign;
        AssertRejected(Move(from, 1, to, 2), RejectReason.ContainerLocked);
    }

    [Fact]
    public void ConfigurePlayerBagWithTransferIntoNewSlot_OwnerGetsFullState()
    {
        var bag = Bag;
        var size = (ushort)bag.Size;
        var newSlot = (ushort)(size + 1);

        // The bigger bag and an item in a slot that exists only after the change: a delta alone would name a slot outside the old size.
        _sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerConfigureOp(bag.Id, (ushort)(size + 4), bag.Access),
            new ItemCreateOp(new SlotRef(bag.Id, newSlot), Gem, 3, 5, ItemSource.Admin),
        ]));

        var state = Assert.Single(_sent.OfType<ContainerState>());
        Assert.Equal(bag.Id, state.Container);
        Assert.Equal(size + 4, state.Size);
        Assert.Contains(state.Slots, slot => slot.Slot == newSlot && slot.Stack.Count == 3);
        Assert.Empty(_sent.OfType<ContainerDelta>());
    }

    [Theory]
    [InlineData(0, 0, 3, RejectReason.InvalidParameters)]
    [InlineData(40, 1, 3, RejectReason.InvalidParameters)]
    [InlineData(0, 4, 3, RejectReason.NotEnoughItems)]
    [InlineData(0, 65535, 3, RejectReason.NotEnoughItems)]
    [InlineData(0, 1, 2, RejectReason.ContainerChanged)]
    public void Submit_InvalidMove_RejectsWithoutChanges(int slot, int count, int expectedCount, RejectReason reason)
    {
        var intent = Move(Hotbar, 1, Bag, (ushort)slot) with { Count = (ushort)count, ExpectedCount = (ushort)expectedCount };
        AssertRejected(intent, reason);
    }

    [Fact]
    public void Submit_UnregisteredExpectedType_RejectsWithoutChanges()
    {
        AssertRejected(Move(Hotbar, 1, Bag, 0) with { ExpectedType = new ItemTypeId(999) }, RejectReason.ContainerChanged);
    }

    [Fact]
    public void Submit_TargetNearStackLimit_MovesOnlyAvailableCapacity()
    {
        CreateGems(Bag, 0, 4);
        var before = _journal.Entries.Count;
        var result = _sim.Submit(_player, Move(Hotbar, 1, Bag, 0) with { Count = 3 });
        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal((ushort)5, Bag.GetSlot(0).Count);
        Assert.Equal((ushort)2, Hotbar.GetSlot(1).Count);
        Assert.Equal(before + 1, _journal.Entries.Count);
        Assert.Equal((ushort)1, Assert.IsType<ItemTransferOp>(_journal.Entries[^1].Operation).Count);
        AssertRejected(Move(Hotbar, 1, Bag, 0) with { ExpectedCount = 2 }, RejectReason.InventoryFull);
    }

    [Fact]
    public void ApplyServerOperation_FailingSecondTransfer_RestoresSlotsVersionsHashAndJournal()
    {
        var hash = StateHasher.Hash(_sim.State);
        var count = _journal.Entries.Count;
        var versions = (Bag.Version, Hotbar.Version);
        var compound = new CompoundOp([
            new ItemTransferOp(new SlotRef(Hotbar.Id, 1), new SlotRef(Bag.Id, 0), Gem, 1, 5),
            new ItemTransferOp(new SlotRef(Hotbar.Id, 1), new SlotRef(Bag.Id, 1), Gem, 3, 5),
        ]);

        Assert.Throws<InvalidOperationException>(() => _sim.ApplyServerOperation(compound));

        Assert.Equal(hash, StateHasher.Hash(_sim.State));
        Assert.Equal(count, _journal.Entries.Count);
        Assert.Equal(versions, (Bag.Version, Hotbar.Version));
        Assert.True(Bag.GetSlot(0).IsEmpty);
        Assert.Equal(new ItemStack(Gem, 3), Hotbar.GetSlot(1));
        Assert.Empty(_sent);
        Assert.True(_sim.Submit(_player, Move(Hotbar, 1, Bag, 0)).IsAccepted);
    }

    [Fact]
    public void Submit_RepeatedStaleMove_DoesNotDuplicateItemsOrJournalEntry()
    {
        var intent = Move(Hotbar, 1, Bag, 0);
        Assert.True(_sim.Submit(_player, intent).IsAccepted);
        AssertRejected(intent, RejectReason.ContainerChanged);
        Assert.Equal(new ItemStack(Gem, 1), Bag.GetSlot(0));
        Assert.Equal(new ItemStack(Gem, 2), Hotbar.GetSlot(1));
    }

    [Fact]
    public void Replay_OrderedInventoryJournal_RestoresHashAndRejectsDuplicatesAndGaps()
    {
        Assert.True(_sim.Submit(_player, Move(Hotbar, 1, Bag, 0)).IsAccepted);
        Assert.True(_sim.Submit(_player, Move(Bag, 0, Hotbar, 1) with { ExpectedCount = 1 }).IsAccepted);
        var replayJournal = new InMemoryJournal();
        var restarted = NewSimulation(new FakeTerrainService(), replayJournal);
        foreach (var entry in _journal.Entries)
        {
            var writer = new ByteWriter();
            OperationCodec.Write(writer, entry.Operation);
            var reader = new ByteReader(writer.WrittenSpan);
            restarted.Replay(entry with { Operation = OperationCodec.Read(ref reader) });
            Assert.Equal(0, reader.Remaining);
        }

        var hash = StateHasher.Hash(restarted.State);
        Assert.Equal(StateHasher.Hash(_sim.State), hash);
        Assert.Empty(replayJournal.Entries);
        Assert.Throws<ReplayFailedException>(() => restarted.Replay(_journal.Entries[^1]));
        Assert.Equal(hash, StateHasher.Hash(restarted.State));
        var empty = NewSimulation(new FakeTerrainService(), new InMemoryJournal());
        var emptyHash = StateHasher.Hash(empty.State);
        Assert.Throws<ReplayFailedException>(() => empty.Replay(_journal.Entries[1]));
        Assert.Equal(emptyHash, StateHasher.Hash(empty.State));
    }

    [Fact]
    public void Submit_ItemBurstExhausted_RejectsThenRefillsAtConfiguredTickRate()
    {
        for (var index = 0; index < _sim.Rules.ItemIntentBurst; index++)
        {
            var intent = index % 2 == 0
                ? Move(Hotbar, 0, Bag, 0) with { ExpectedType = WorldFactory.Item(SurvivalContentFactory.ToolKey), ExpectedCount = 1 }
                : Move(Bag, 0, Hotbar, 0) with { ExpectedType = WorldFactory.Item(SurvivalContentFactory.ToolKey), ExpectedCount = 1 };
            Assert.True(_sim.Submit(_player, intent).IsAccepted);
        }

        AssertRejected(Move(Hotbar, 1, Bag, 1), RejectReason.RateLimited);
        var drop = new ItemDropIntent(new SlotRef(Hotbar.Id, 1), 1, Gem, 3);
        var count = _journal.Entries.Count;
        var hash = StateHasher.Hash(_sim.State);
        Assert.Equal(RejectReason.RateLimited, _sim.Submit(_player, drop).Reason);
        Assert.Equal(count, _journal.Entries.Count);
        Assert.Equal(hash, StateHasher.Hash(_sim.State));
        for (var tick = 0; tick < _sim.Rules.TickRate / _sim.Rules.ItemIntentsPerSecond; tick++)
        {
            _sim.AdvanceTick();
        }

        Assert.True(_sim.Submit(_player, Move(Hotbar, 1, Bag, 1)).IsAccepted);
        AssertRejected(Move(Hotbar, 1, Bag, 2) with { ExpectedCount = 2 }, RejectReason.RateLimited);
    }

    [Fact]
    public void Submit_ZeroDropCount_RejectsWithoutJournalOrDelta()
    {
        var hash = StateHasher.Hash(_sim.State);
        var count = _journal.Entries.Count;
        var result = _sim.Submit(_player, new ItemDropIntent(new SlotRef(Hotbar.Id, 1), 0, Gem, 3));
        Assert.False(result.IsAccepted);
        Assert.Equal(RejectReason.InvalidParameters, result.Reason);
        Assert.Equal(hash, StateHasher.Hash(_sim.State));
        Assert.Equal(count, _journal.Entries.Count);
        Assert.Empty(_sent);
    }

    [Fact]
    public void OnPlayerLeaving_InventoryChanges_StopDeltasUntilReadyRestoresCurrentState()
    {
        _service.OnPlayerLeaving(_player, DisconnectCode.Normal);
        _sim.LeaveAllOnlinePlayers();
        CreateGems(Bag, 0, 2);
        Assert.Empty(_sent);
        var rejoined = Join(1);
        Assert.Equal(_player, rejoined);
        var hash = StateHasher.Hash(_sim.State);
        var count = _journal.Entries.Count;

        _service.OnPlayerReady(rejoined);

        Assert.Equal(hash, StateHasher.Hash(_sim.State));
        Assert.Equal(count, _journal.Entries.Count);
        Assert.IsType<ItemRegistry>(_sent[0]);
        var states = _sent.Skip(1).Select(message => Assert.IsType<ContainerState>(message)).ToArray();
        Assert.Equal(3, states.Length);
        var bag = Assert.Single(states, state => state.Container == Bag.Id);
        Assert.Equal(Bag.Version, bag.Version);
        Assert.Equal([new SlotContent(0, new ItemStack(Gem, 2))], bag.Slots);
        _sent.Clear();
        CreateGems(Bag, 0, 1);
        Assert.Equal(Bag.Version, Assert.IsType<ContainerDelta>(Assert.Single(_sent)).Version);
    }

    [Fact]
    public void Handle_VersionGapResubscription_ReturnsFullCurrentStateWithoutMutation()
    {
        var requests = new List<ContainerId>();
        var client = new ClientInventory(SurvivalContentFactory.CreateTest().Items, requests.Add, NullLogger<ClientInventory>.Instance);
        _service.OnPlayerReady(_player);
        foreach (var message in _sent)
        {
            client.OnMessage(message);
        }

        _sent.Clear();
        CreateGems(Bag, 0, 1);
        _sent.Clear(); // Simulate a missing version at the receiver.
        CreateGems(Bag, 0, 1);
        client.OnMessage(Assert.Single(_sent));
        Assert.Equal(Bag.Id, Assert.Single(requests));
        _sent.Clear();
        var hash = StateHasher.Hash(_sim.State);
        var count = _journal.Entries.Count;

        Assert.True(_service.Handle(_player, new ContainerSubscribe(requests[0])));
        var full = Assert.IsType<ContainerState>(Assert.Single(_sent));
        Assert.Equal(Bag.Version, full.Version);
        Assert.Equal([new SlotContent(0, new ItemStack(Gem, 2))], full.Slots);
        client.OnMessage(full);
        Assert.Equal(Bag.Version, client.Bag!.Version);
        Assert.Equal(5, client.CountOf(Gem));
        Assert.Equal(hash, StateHasher.Hash(_sim.State));
        Assert.Equal(count, _journal.Entries.Count);
    }

    [Fact]
    public void Submit_FullInventoryMining_AcceptsTerrainWithoutChangingExistingStacksPerPlanD11()
    {
        var stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
        var operations = new List<Operation>();
        foreach (var container in new[] { Bag, Hotbar })
        {
            for (ushort slot = 0; slot < container.Size; slot++)
            {
                var stack = container.GetSlot(slot);
                var type = stack.IsEmpty ? stone : stack.Type;
                ushort limit = type == stone ? (ushort)500 : type == Gem ? (ushort)5 : (ushort)1;
                if (stack.Count < limit)
                {
                    operations.Add(new ItemCreateOp(new SlotRef(container.Id, slot), type, (ushort)(limit - stack.Count), limit, ItemSource.Admin));
                }
            }
        }

        _sim.ApplyServerOperation(new CompoundOp(operations));
        var versions = (Bag.Version, Hotbar.Version);
        _sent.Clear();
        _terrain.MeasureResult = MeasuredSphere.SolidOf(1, 3);
        Assert.True(_sim.State.Players.TryGet(_player, out var player));
        var eye = player.EyePosition(_sim.Rules);
        var surface = new Vector3(0f, _terrain.SurfaceRadius, 0f);
        var count = _journal.Entries.Count;

        var result = _sim.Submit(_player, new TerrainToolIntent(WorldFactory.HomePlanet, eye,
            Vector3.Normalize(surface - eye), 1f, TerrainEditMode.Remove, 0));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.Equal(count + 1, _journal.Entries.Count);
        Assert.IsType<TerrainEditOp>(_journal.Entries[^1].Operation);
        Assert.Equal(versions, (Bag.Version, Hotbar.Version));
        Assert.All(Enumerable.Range(0, Bag.Size), slot => Assert.Equal(new ItemStack(stone, 500), Bag.GetSlot(slot)));
        Assert.Empty(_sent);
    }

    private Container Bag => GetContainer(_player, ContainerKind.PlayerBag);
    private Container Hotbar => GetContainer(_player, ContainerKind.Hotbar);
    private static ItemTypeId Gem => WorldFactory.Item(SurvivalContentFactory.GemKey);

    private static ServerSimulation NewSimulation(FakeTerrainService terrain, InMemoryJournal journal) => new(
        WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey],
            ["stone", "dirt"], SurvivalContentFactory.ItemKeys), terrain, journal, new GameRules(),
        SurvivalContentFactory.CreateTest(), NullLogger<ServerSimulation>.Instance);

    private PlayerId Join(ulong identity)
    {
        var join = _sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, identity), $"p{identity}");
        _sim.ApplyServerOperation(join);
        return join.Player;
    }

    private Container GetContainer(PlayerId player, ContainerKind kind)
    {
        Assert.True(_sim.State.Containers.TryGetOfPlayer(player, kind, out var container));
        return container;
    }

    private static ItemMoveIntent Move(Container from, ushort source, Container to, ushort target) =>
        new(new SlotRef(from.Id, source), ItemMoveTarget.ToSlot(new SlotRef(to.Id, target)), 1, Gem, 3);

    private void CreateGems(Container container, ushort slot, ushort count) =>
        _sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(container.Id, slot), Gem, count, 5, ItemSource.Admin));

    private void AssertRejected(ItemMoveIntent intent, RejectReason reason)
    {
        var hash = StateHasher.Hash(_sim.State);
        var count = _journal.Entries.Count;
        _sent.Clear();
        var result = _sim.Submit(_player, intent);
        Assert.False(result.IsAccepted);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(hash, StateHasher.Hash(_sim.State));
        Assert.Equal(count, _journal.Entries.Count);
        Assert.Empty(_sent);
    }
}
