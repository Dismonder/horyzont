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

public sealed class InventoryServiceTests
{
    private const byte StoneMaterial = 1;

    private readonly InMemoryJournal _journal = new();
    private readonly FakeTerrainService _terrain = new();
    private readonly ServerSimulation _sim;
    private readonly List<(PlayerId Player, Message Message)> _sent = [];
    private readonly InventoryService _service;
    private bool _readOnly;

    public InventoryServiceTests()
    {
        var world = WorldState.CreateNew(
            WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey], ["stone", "dirt"],
            SurvivalContentFactory.ItemKeys);
        _sim = new ServerSimulation(
            world, _terrain, _journal, new GameRules(), SurvivalContentFactory.CreateTest(), NullLogger<ServerSimulation>.Instance);
        _service = new InventoryService(
            () => _sim, (player, message) => _sent.Add((player, message)), () => _readOnly, NullLogger<InventoryService>.Instance);
    }

    private PlayerId Join(ulong identity)
    {
        var join = _sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, identity), $"p{identity}");
        _sim.ApplyServerOperation(join);
        return join.Player;
    }

    private List<Message> SentTo(PlayerId player) => _sent.Where(s => s.Player == player).Select(s => s.Message).ToList();

    private Container Container(PlayerId player, ContainerKind kind)
    {
        Assert.True(_sim.State.Containers.TryGetOfPlayer(player, kind, out var container));
        return container;
    }

    [Fact]
    public void FirstReady_CreatesThreeContainersAndStarterKit_OneJournalEntry()
    {
        var player = Join(1);
        var before = _journal.Entries.Count;

        _service.OnPlayerReady(player);

        Assert.Equal(before + 1, _journal.Entries.Count);
        var compound = Assert.IsType<CompoundOp>(_journal.Entries[^1].Operation);
        Assert.Equal(
            [typeof(ContainerCreateOp), typeof(ContainerCreateOp), typeof(ContainerCreateOp), typeof(ItemCreateOp), typeof(ItemCreateOp)],
            compound.Components.Select(c => c.GetType()));
        Assert.Equal(3, _sim.State.Containers.Count);
        var bag = Container(player, ContainerKind.PlayerBag);
        var hotbar = Container(player, ContainerKind.Hotbar);
        var equipment = Container(player, ContainerKind.Equipment);
        Assert.Equal([40, 10, 7], [bag.Size, hotbar.Size, equipment.Size]);
        Assert.All([bag, hotbar, equipment], c => Assert.Equal((player, ContainerAccess.Private), (c.Owner, c.Access)));
        Assert.Equal(new ItemStack(WorldFactory.Item(SurvivalContentFactory.ToolKey), 1), hotbar.GetSlot(0));
        Assert.Equal(new ItemStack(WorldFactory.Item(SurvivalContentFactory.GemKey), 3), hotbar.GetSlot(1));
        Assert.All(compound.Components.OfType<ItemCreateOp>(), c => Assert.Equal(ItemSource.Starter, c.Source));
    }

    [Fact]
    public void SecondReady_NoNewContainers()
    {
        var player = Join(1);
        _service.OnPlayerReady(player);
        var journalCount = _journal.Entries.Count;
        _sent.Clear();

        _service.OnPlayerReady(player);

        Assert.Equal(journalCount, _journal.Entries.Count);
        Assert.Equal(3, _sim.State.Containers.Count);
        Assert.Equal(4, SentTo(player).Count);
    }

    [Fact]
    public void Ready_SendsItemRegistryThenThreeStates()
    {
        var player = Join(1);

        _service.OnPlayerReady(player);

        var messages = SentTo(player);
        Assert.Equal(4, messages.Count);
        var registry = Assert.IsType<ItemRegistry>(messages[0]);
        Assert.Equal(_sim.State.ItemTypes.Entries, registry.Entries);
        var bag = Assert.IsType<ContainerState>(messages[1]);
        var hotbar = Assert.IsType<ContainerState>(messages[2]);
        var equipment = Assert.IsType<ContainerState>(messages[3]);
        Assert.Equal(
            [ContainerKind.PlayerBag, ContainerKind.Hotbar, ContainerKind.Equipment], [bag.Kind, hotbar.Kind, equipment.Kind]);
        Assert.Equal(Container(player, ContainerKind.PlayerBag).Id, bag.Container);
        Assert.Equal((40, 1U, RejectReason.None), (bag.Size, bag.Version, bag.Reason));
        Assert.Empty(bag.Slots);
        Assert.Equal(
            [
                new SlotContent(0, new ItemStack(WorldFactory.Item(SurvivalContentFactory.ToolKey), 1)),
                new SlotContent(1, new ItemStack(WorldFactory.Item(SurvivalContentFactory.GemKey), 3)),
            ],
            hotbar.Slots);
        Assert.Equal(1U, hotbar.Version);
        Assert.Equal((7, ContainerAccess.Private), (equipment.Size, equipment.Access));
    }

    [Fact]
    public void AppliedDig_SendsDeltaToOwnerOnly()
    {
        var digger = Join(1);
        var other = Join(2);
        _service.OnPlayerReady(digger);
        _service.OnPlayerReady(other);
        _sent.Clear();
        _terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);
        _sim.State.Players.TryGet(digger, out var state);
        var eye = state!.EyePosition(_sim.Rules);
        var surface = new Vector3(0f, _terrain.SurfaceRadius, 0f);

        var result = _sim.Submit(
            digger, new TerrainToolIntent(WorldFactory.HomePlanet, eye, Vector3.Normalize(surface - eye), 1f, TerrainEditMode.Remove, 0));

        Assert.True(result.IsAccepted, $"{result.Reason}");
        var delta = Assert.IsType<ContainerDelta>(Assert.Single(SentTo(digger)));
        var bag = Container(digger, ContainerKind.PlayerBag);
        Assert.Equal(bag.Id, delta.Container);
        Assert.Equal(2U, delta.Version);
        Assert.Equal([new SlotContent(0, new ItemStack(WorldFactory.Item(SurvivalContentFactory.StoneKey), 6))], delta.Changed);
        Assert.Empty(SentTo(other));
    }

    [Fact]
    public void SubscribeOwn_ResendsState()
    {
        var player = Join(1);
        _service.OnPlayerReady(player);
        var hotbar = Container(player, ContainerKind.Hotbar);
        _sent.Clear();

        _service.Handle(player, new ContainerSubscribe(hotbar.Id));

        var state = Assert.IsType<ContainerState>(Assert.Single(SentTo(player)));
        Assert.Equal((hotbar.Id, RejectReason.None, ContainerKind.Hotbar, hotbar.Version), (state.Container, state.Reason, state.Kind, state.Version));
        Assert.Equal(2, state.Slots.Count);
    }

    [Fact]
    public void SubscribeForeign_ContainerLocked()
    {
        var player = Join(1);
        var other = Join(2);
        _service.OnPlayerReady(player);
        _service.OnPlayerReady(other);
        var foreign = Container(other, ContainerKind.PlayerBag);
        _sent.Clear();

        _service.Handle(player, new ContainerSubscribe(foreign.Id));
        _service.Handle(player, new ContainerSubscribe(new ContainerId(9999)));
        _service.Handle(player, new ContainerUnsubscribe(Container(player, ContainerKind.PlayerBag).Id));

        Assert.Equal(
            [ContainerState.Rejected(foreign.Id, RejectReason.ContainerLocked), ContainerState.Rejected(new ContainerId(9999), RejectReason.ContainerLocked)],
            SentTo(player));
    }

    [Fact]
    public void Ready_PartialContainerSet_CreatesEachMissingContainerInOneCompound()
    {
        var player = Join(1);
        var existing = _sim.State.ContainerIds.PeekNext();
        _sim.ApplyServerOperation(
            new ContainerCreateOp(existing, ContainerKind.Hotbar, ContainerHost.Player(player), player, ContainerAccess.Private, 10));
        var before = _journal.Entries.Count;

        _service.OnPlayerReady(player);

        Assert.Equal(before + 1, _journal.Entries.Count);
        var compound = Assert.IsType<CompoundOp>(_journal.Entries[^1].Operation);
        Assert.Equal(2, compound.Components.OfType<ContainerCreateOp>().Count());
        Assert.Equal(3, _sim.State.Containers.Count);
        Assert.Equal(existing, Container(player, ContainerKind.Hotbar).Id);
        Assert.Equal(40, Container(player, ContainerKind.PlayerBag).Size);
        Assert.Equal(7, Container(player, ContainerKind.Equipment).Size);
        Assert.Equal(1U, Container(player, ContainerKind.Hotbar).Version);
    }

    [Fact]
    public void ReadOnly_CreatesNothing_SendsExisting()
    {
        var known = Join(1);
        _service.OnPlayerReady(known);
        _readOnly = true;
        var fresh = Join(2);
        var journalCount = _journal.Entries.Count;
        _sent.Clear();

        _service.OnPlayerReady(fresh);
        _service.OnPlayerReady(known);

        Assert.Equal(journalCount, _journal.Entries.Count);
        Assert.Equal(3, _sim.State.Containers.Count);
        Assert.Equal([typeof(ItemRegistry)], SentTo(fresh).Select(m => m.GetType()));
        Assert.Equal(
            [typeof(ItemRegistry), typeof(ContainerState), typeof(ContainerState), typeof(ContainerState)], SentTo(known).Select(m => m.GetType()));
    }
}
