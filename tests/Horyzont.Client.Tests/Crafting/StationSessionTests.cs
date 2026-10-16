using Horyzont.Client.Blocks;
using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Crafting;

public sealed class StationSessionTests
{
    private static readonly BlockRef _furnace = new(new EntityId(100), new CellPos(4, 1, 0));

    private sealed class Rig
    {
        private ulong _tick;
        private bool _tickKnown = true;

        public Rig()
        {
            Fixture = new CraftingClientFixture();
            Entities = new ClientBlockEntities();
            Session = new StationSession(
                Fixture.Inventory, Entities, Fixture.Content, Fixture.Sender, Sent.Add, ReadTick, new GameRules(), NullLogger<StationSession>.Instance);
            Interaction = new InventoryInteraction(
                Fixture.Inventory, Fixture.Sender, NullLogger<InventoryInteraction>.Instance, null, Session);
            Replicate(string.Empty, null);
        }

        public CraftingClientFixture Fixture { get; }

        public ClientBlockEntities Entities { get; }

        public StationSession Session { get; }

        public InventoryInteraction Interaction { get; }

        public List<Message> Sent { get; } = [];

        public int Opened { get; private set; }

        public int Closed { get; private set; }

        public Rig Watch()
        {
            Session.Opened += () => Opened++;
            Session.Closed += () => Closed++;
            return this;
        }

        public void SetTick(ulong? tick)
        {
            _tickKnown = tick is not null;
            _tick = tick ?? 0;
        }

        public void Replicate(string recipe, ulong? started) =>
            Entities.Apply(
                new BlockEntityStates(
                    _furnace.Construct,
                    [new BlockEntityEntry(_furnace.Cell, new StationBlockState(CraftingClientFixture.InputId, CraftingClientFixture.OutputId, recipe, started is { } s ? new Tick(s) : null))]));

        public void InputState(params (int Slot, string Item, int Count)[] slots) =>
            Fixture.State(CraftingClientFixture.InputId, ContainerKind.StationInput, 4, slots);

        public void OutputState(params (int Slot, string Item, int Count)[] slots) =>
            Fixture.State(CraftingClientFixture.OutputId, ContainerKind.StationOutput, 2, slots);

        public void OpenAndArrive()
        {
            Assert.True(Session.Open(StationKind.Furnace, _furnace));
            InputState();
            OutputState();
        }

        private bool ReadTick(out ulong tick)
        {
            tick = _tick;
            return _tickKnown;
        }
    }

    [Fact]
    public void Open_SubscribesBothContainers()
    {
        var rig = new Rig().Watch();

        Assert.True(rig.Session.Open(StationKind.Furnace, _furnace));

        Assert.Equal(
            [new ContainerSubscribe(CraftingClientFixture.InputId), new ContainerSubscribe(CraftingClientFixture.OutputId)], rig.Sent);
        Assert.True(rig.Session.IsOpening);
        Assert.False(rig.Session.IsOpen);
        Assert.Equal(StationKind.Furnace, rig.Session.Kind);
        Assert.Equal(_furnace, rig.Session.Station);

        rig.InputState((0, SurvivalContentFactory.OreKey, 4));

        Assert.True(rig.Session.IsOpening);
        Assert.Equal(0, rig.Opened);

        rig.OutputState((1, SurvivalContentFactory.IngotKey, 2));

        Assert.True(rig.Session.IsOpen);
        Assert.False(rig.Session.IsOpening);
        Assert.Equal(1, rig.Opened);
        Assert.Equal(new ItemStack(CraftingClientFixture.Item(SurvivalContentFactory.OreKey), 4), rig.Session.Input!.Get(0));
        Assert.Equal(new ItemStack(CraftingClientFixture.Item(SurvivalContentFactory.IngotKey), 2), rig.Session.Output!.Get(1));
    }

    [Fact]
    public void Open_StateOfTheBlockNotReplicated_ReturnsFalseAndSendsNothing()
    {
        var rig = new Rig();
        rig.Entities.Remove(_furnace.Construct);

        Assert.False(rig.Session.Open(StationKind.Furnace, _furnace));

        Assert.Empty(rig.Sent);
        Assert.False(rig.Session.IsOpening);
        Assert.Null(rig.Session.Station);
    }

    [Fact]
    public void Close_UnsubscribesBoth()
    {
        var rig = new Rig().Watch();
        rig.OpenAndArrive();
        rig.Sent.Clear();

        rig.Session.Close();

        Assert.Equal(
            [new ContainerUnsubscribe(CraftingClientFixture.InputId), new ContainerUnsubscribe(CraftingClientFixture.OutputId)], rig.Sent);
        Assert.False(rig.Session.IsOpen);
        Assert.Null(rig.Session.Station);
        Assert.Null(rig.Session.Input);
        Assert.False(rig.Fixture.Inventory.TryGetContainer(CraftingClientFixture.InputId, out _));
        Assert.False(rig.Fixture.Inventory.TryGetContainer(CraftingClientFixture.OutputId, out _));
        Assert.Equal(1, rig.Closed);

        rig.Session.Close();
        Assert.Equal(1, rig.Closed);
        Assert.Equal(2, rig.Sent.Count);
    }

    [Fact]
    public void Close_WhileOpening_Unsubscribes()
    {
        var rig = new Rig().Watch();
        Assert.True(rig.Session.Open(StationKind.Furnace, _furnace));
        rig.Sent.Clear();

        rig.Session.Close();

        Assert.Equal(2, rig.Sent.Count);
        Assert.False(rig.Session.IsOpening);

        // A state that arrives after the panel was closed is not followed.
        rig.InputState();
        Assert.False(rig.Fixture.Inventory.TryGetContainer(CraftingClientFixture.InputId, out _));
        Assert.Equal(0, rig.Opened);
    }

    [Fact]
    public void ServerOutOfReach_ClosesPanel()
    {
        var rig = new Rig().Watch();
        rig.OpenAndArrive();

        rig.Fixture.Inventory.OnMessage(
            new ContainerState(CraftingClientFixture.OutputId, RejectReason.OutOfReach, ContainerKind.StationOutput, ContainerAccess.Shared, 1, 0, []));

        Assert.False(rig.Session.IsOpen);
        Assert.Equal(1, rig.Closed);
        Assert.Equal(RejectReason.OutOfReach, rig.Session.LastRefusal);
        Assert.False(rig.Fixture.Inventory.TryGetContainer(CraftingClientFixture.InputId, out _));
    }

    [Fact]
    public void SelectRecipe_SendsCraftCount1()
    {
        var rig = new Rig();
        rig.OpenAndArrive();

        Assert.True(rig.Session.SelectRecipe(rig.Fixture.Recipe("t:smelt")));

        Assert.Equal(new CraftIntent("t:smelt", 1, _furnace), Assert.Single(rig.Fixture.Sender.Intents));
    }

    [Fact]
    public void SelectRecipe_OfAnotherStationKindOrClosed_SendsNothing()
    {
        var rig = new Rig();
        Assert.False(rig.Session.SelectRecipe(rig.Fixture.Recipe("t:smelt")));

        rig.OpenAndArrive();
        Assert.False(rig.Session.SelectRecipe(rig.Fixture.Recipe("t:refine")));
        Assert.False(rig.Session.SelectRecipe(rig.Fixture.Recipe("t:hand")));

        Assert.Empty(rig.Fixture.Sender.Intents);
    }

    [Fact]
    public void Progress_FromStartedAndServerTick()
    {
        var rig = new Rig();
        rig.OpenAndArrive();
        rig.Replicate("t:smelt", 100);

        // t:smelt takes one second: 60 ticks.
        rig.SetTick(100);
        Assert.Equal(0.0, rig.Session.Progress!.Value, 6);
        rig.SetTick(130);
        Assert.Equal(0.5, rig.Session.Progress!.Value, 6);
        rig.SetTick(190);
        Assert.Equal(0.5, rig.Session.Progress!.Value, 6);
        rig.SetTick(90);
        Assert.Equal(0.0, rig.Session.Progress!.Value, 6);
        Assert.Equal("t:smelt", rig.Session.ActiveRecipe!.Key);

        rig.SetTick(null);
        Assert.Null(rig.Session.Progress);
    }

    [Fact]
    public void Progress_IdleStationOrUnknownRecipe_IsNull()
    {
        var rig = new Rig();
        rig.OpenAndArrive();
        rig.SetTick(500);

        Assert.Null(rig.Session.ActiveRecipe);
        Assert.Null(rig.Session.Progress);

        rig.Replicate("t:smelt", null);
        Assert.NotNull(rig.Session.ActiveRecipe);
        Assert.Null(rig.Session.Progress);

        rig.Replicate("t:gone", 100);
        Assert.Null(rig.Session.ActiveRecipe);
        Assert.Null(rig.Session.Progress);
    }

    [Fact]
    public void MoveIntoOutput_NotOffered()
    {
        var rig = new Rig();
        rig.OpenAndArrive();
        rig.Fixture.Put(CraftingClientFixture.Bag(0), SurvivalContentFactory.OreKey, 4);

        // The control: the same stack goes into the input.
        Assert.Equal(InteractionOutcome.Picked, rig.Interaction.LeftClick(CraftingClientFixture.Bag(0)));
        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.LeftClick(new SlotRef(CraftingClientFixture.InputId, 1)));
        Assert.IsType<ItemMoveIntent>(Assert.Single(rig.Fixture.Sender.Intents));
        rig.Fixture.Sender.Intents.Clear();
        rig.Interaction.Cancel();

        rig.Fixture.Put(CraftingClientFixture.Bag(1), SurvivalContentFactory.OreKey, 4);
        Assert.Equal(InteractionOutcome.Picked, rig.Interaction.LeftClick(CraftingClientFixture.Bag(1)));
        Assert.Equal(InteractionOutcome.Ignored, rig.Interaction.LeftClick(new SlotRef(CraftingClientFixture.OutputId, 0)));
        Assert.Equal(InteractionOutcome.Ignored, rig.Interaction.RightClick(new SlotRef(CraftingClientFixture.OutputId, 1)));

        Assert.Empty(rig.Fixture.Sender.Intents);
        Assert.NotNull(rig.Interaction.Held);
    }

    [Fact]
    public void Output_IsASourceOfMoves()
    {
        var rig = new Rig();
        rig.Session.Open(StationKind.Furnace, _furnace);
        rig.InputState();
        rig.OutputState((0, SurvivalContentFactory.IngotKey, 2));

        Assert.Equal(InteractionOutcome.Picked, rig.Interaction.LeftClick(new SlotRef(CraftingClientFixture.OutputId, 0)));
        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.LeftClick(CraftingClientFixture.Bag(3)));

        var move = Assert.IsType<ItemMoveIntent>(Assert.Single(rig.Fixture.Sender.Intents));
        Assert.Equal(new SlotRef(CraftingClientFixture.OutputId, 0), move.From);
    }

    [Fact]
    public void ShiftClick_FromTheBagGoesToTheInput_FromTheOutputToTheBag()
    {
        var rig = new Rig();
        rig.Session.Open(StationKind.Furnace, _furnace);
        rig.InputState();
        rig.OutputState((0, SurvivalContentFactory.IngotKey, 2));
        rig.Fixture.Put(CraftingClientFixture.Bag(0), SurvivalContentFactory.OreKey, 4);

        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.ShiftClick(CraftingClientFixture.Bag(0)));
        Assert.Equal(InteractionOutcome.Sent, rig.Interaction.ShiftClick(new SlotRef(CraftingClientFixture.OutputId, 0)));

        var moves = rig.Fixture.Sender.Intents.Cast<ItemMoveIntent>().ToArray();
        Assert.Equal(ItemMoveTarget.AnyIn(CraftingClientFixture.InputId), moves[0].To);
        Assert.Equal(ItemMoveTarget.AnyIn(CraftingClientFixture.BagId), moves[1].To);
    }

    [Fact]
    public void TakeAll_SendsTheIntentForTheOutput()
    {
        var rig = new Rig();
        Assert.False(rig.Session.TakeAll());
        rig.OpenAndArrive();

        Assert.True(rig.Session.TakeAll());

        Assert.Equal(new TakeAllIntent(CraftingClientFixture.OutputId), Assert.Single(rig.Fixture.Sender.Intents));
    }

    [Fact]
    public void Disconnect_ClosesThePanel()
    {
        var rig = new Rig().Watch();
        rig.OpenAndArrive();

        rig.Session.OnDisconnected(DisconnectCode.Normal);

        Assert.False(rig.Session.IsOpen);
        Assert.Equal(1, rig.Closed);
        Assert.Null(rig.Session.Station);
    }
}
