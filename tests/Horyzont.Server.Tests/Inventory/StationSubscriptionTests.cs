using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Server.Tests.Crafting;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Inventory;

/// <summary>
/// Subscriptions to the containers of a station with time (K6-32 D17): the same reach as a chest (6 m to open, 8 m to keep), the settlement of the
/// pieces due before the state is sent, a delta of every change for every subscriber, both containers ended together.
/// </summary>
public sealed class StationSubscriptionTests
{
    private readonly CraftingServerRig _rig = new();

    public StationSubscriptionTests()
    {
        _rig.ReadyBoth();
        _rig.Sent.Clear();
    }

    [Fact]
    public void SubscribeFurnaceInput_Within6m_SettlesThenSendsState()
    {
        var (input, output) = _rig.PlaceFurnace("t:smelt", new Tick(0));
        _rig.Stock(input, 0, CraftingServerRig.Ore, 4);
        _rig.Stock(input, 1, CraftingServerRig.Peat, 2);
        _rig.Tick(130);
        _rig.Stand(_rig.Alice, 5.9f);
        var journaled = _rig.Journal.Entries.Count;
        _rig.Sent.Clear();

        Assert.True(_rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(input)));

        // Two pieces were due: the settlement is one operation, applied before the state is sent, so the state is the settled one and no delta precedes it.
        Assert.Equal(journaled + 1, _rig.Journal.Entries.Count);
        var state = Assert.IsType<ContainerState>(Assert.Single(_rig.SentTo(_rig.Alice)));
        Assert.Equal((input, RejectReason.None, ContainerKind.StationInput), (state.Container, state.Reason, state.Kind));
        Assert.Equal(CraftingBlocks.InputSlots, state.Size);
        Assert.Empty(state.Slots);
        Assert.Equal(2, _rig.CountIn(output, CraftingServerRig.Ingot));
    }

    [Fact]
    public void SubscribeFurnaceOutput_Beyond6m_OutOfReach()
    {
        var (input, output) = _rig.PlaceFurnace("t:smelt", new Tick(0));
        _rig.Stand(_rig.Alice, 5.9f);

        // The accepted counterpart: 5.9 m opens the output, 6.5 m refuses the same subscription.
        _rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(output));
        Assert.Equal(ContainerKind.StationOutput, Assert.IsType<ContainerState>(Assert.Single(_rig.SentTo(_rig.Alice))).Kind);
        _rig.Sent.Clear();

        _rig.Stand(_rig.Bob, 6.5f);
        _rig.Inventory.Handle(_rig.Bob, new ContainerSubscribe(output));
        _rig.Inventory.Handle(_rig.Bob, new ContainerSubscribe(input));
        _rig.Stock(input, 3, CraftingServerRig.Ore, 1);

        Assert.Equal([ContainerState.Rejected(output, RejectReason.OutOfReach), ContainerState.Rejected(input, RejectReason.OutOfReach)], _rig.SentTo(_rig.Bob));
    }

    [Fact]
    public void WalkAway_EndsBothSubscriptions()
    {
        var (input, output) = _rig.PlaceFurnace();
        _rig.Stand(_rig.Alice, 5f);
        _rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(input));
        _rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(output));
        _rig.Sent.Clear();

        // Between 6 m and 8 m both are kept (opening needs 6 m, keeping 8 m); beyond 8 m both end, each with a state that carries the refusal.
        _rig.Stand(_rig.Alice, 7.5f);
        _rig.Inventory.CheckSubscriptions();
        Assert.Empty(_rig.SentTo(_rig.Alice));

        _rig.Stand(_rig.Alice, 8.5f);
        _rig.Inventory.CheckSubscriptions();
        _rig.Inventory.CheckSubscriptions();

        var states = _rig.SentTo(_rig.Alice).Cast<ContainerState>().ToList();
        Assert.Equal(2, states.Count);
        Assert.Contains(ContainerState.Rejected(input, RejectReason.OutOfReach), states);
        Assert.Contains(ContainerState.Rejected(output, RejectReason.OutOfReach), states);
        var observed = new List<Core.BlockEntities.BlockRef>();
        _rig.Inventory.CollectObservedStations(observed);
        Assert.Empty(observed);
    }

    [Fact]
    public void Delta_ToAllSubscribers()
    {
        var (input, _) = _rig.PlaceFurnace();
        _rig.Stand(_rig.Alice, 4f);
        _rig.Stand(_rig.Bob, 5f);
        _rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(input));
        _rig.Inventory.Handle(_rig.Bob, new ContainerSubscribe(input));
        _rig.Sent.Clear();

        _rig.Stock(input, 2, CraftingServerRig.Ore, 3);

        foreach (var player in new[] { _rig.Alice, _rig.Bob })
        {
            var delta = Assert.IsType<ContainerDelta>(Assert.Single(_rig.SentTo(player)));
            Assert.Equal(input, delta.Container);
            Assert.Equal(new SlotContent(2, new ItemStack(CraftingServerRig.Ore, 3)), Assert.Single(delta.Changed));
        }
    }

    [Fact]
    public void ObservedStations_AreTheBlocksWithSubscribers_InBlockOrder()
    {
        var (input, output) = _rig.PlaceFurnace();
        _rig.Stand(_rig.Alice, 4f);
        var observed = new List<Core.BlockEntities.BlockRef>();

        // Nobody subscribed: nothing observed. Both containers of one station are one station, listed once.
        _rig.Inventory.CollectObservedStations(observed);
        Assert.Empty(observed);

        _rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(input));
        _rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(output));
        _rig.Inventory.CollectObservedStations(observed);

        Assert.Equal([new Core.BlockEntities.BlockRef(FunctionalBlocks.Construct, CraftingServerRig.FurnaceCell)], observed);
    }
}
