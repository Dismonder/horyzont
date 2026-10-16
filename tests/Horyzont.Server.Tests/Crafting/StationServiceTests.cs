using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Server.Tests.Crafting;

/// <summary>
/// The station service (K6-32 D16c): the stations that somebody has open are settled every 10 ticks by a server operation; the others do not tick.
/// The furnace of the rig smelts one ingot from two ore and one peat in 60 ticks (recipe <c>t:smelt</c>).
/// </summary>
public sealed class StationServiceTests
{
    private readonly CraftingServerRig _rig = new();

    public StationServiceTests()
    {
        _rig.ReadyBoth();
        _rig.Sent.Clear();
    }

    private (ContainerId Input, ContainerId Output) SmeltingFurnace(Tick started, ushort ore = 2, ushort peat = 1)
    {
        var (input, output) = _rig.PlaceFurnace("t:smelt", started);
        _rig.Stock(input, 0, CraftingServerRig.Ore, ore);
        _rig.Stock(input, 1, CraftingServerRig.Peat, peat);
        return (input, output);
    }

    private void Observe(ContainerId container)
    {
        _rig.Stand(_rig.Alice, 4f);
        _rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(container));
        _rig.Sent.Clear();
    }

    [Fact]
    public void ObservedFurnace_SettlesWhenItemCompletes()
    {
        var (input, output) = SmeltingFurnace(_rig.Sim.State.CurrentTick);
        Observe(input);
        var journaled = _rig.Journal.Entries.Count;

        // Before the piece is done nothing is written; the piece is done at tick 60 and the service settles it there (60 is a multiple of 10).
        _rig.Tick(59);
        Assert.Equal(journaled, _rig.Journal.Entries.Count);
        _rig.Tick(2);

        Assert.Equal(journaled + 1, _rig.Journal.Entries.Count);
        Assert.Equal(1, _rig.Stations.SettlementsApplied);
        Assert.Equal(1, _rig.CountIn(output, CraftingServerRig.Ingot));
        Assert.Equal(0, _rig.CountIn(input, CraftingServerRig.Ore));
        Assert.Null(_rig.Station().Started);

        // The open panel hears of it: a delta of the input (the ore and the peat are gone), the output is not subscribed.
        var delta = Assert.IsType<ContainerDelta>(Assert.Single(_rig.SentTo(_rig.Alice)));
        Assert.Equal(input, delta.Container);
        Assert.All(delta.Changed, slot => Assert.True(slot.Stack.IsEmpty));
    }

    [Fact]
    public void UnobservedFurnace_NoOperations()
    {
        var (input, output) = SmeltingFurnace(_rig.Sim.State.CurrentTick, ore: 6, peat: 3);
        var journaled = _rig.Journal.Entries.Count;

        // A long time with nobody at the furnace: no operation, no change; the pieces wait in the state until somebody opens it.
        _rig.Tick(600);

        Assert.Equal(journaled, _rig.Journal.Entries.Count);
        Assert.Equal(0, _rig.CountIn(output, CraftingServerRig.Ingot));
        Assert.Equal(6, _rig.CountIn(input, CraftingServerRig.Ore));
        Assert.Equal(0, _rig.Stations.SettlementsApplied);

        // The accepted counterpart: opening the same furnace settles all three pieces at once, before the state is sent.
        Observe(input);
        _rig.Tick(10);
        Assert.Equal(3, _rig.CountIn(output, CraftingServerRig.Ingot));
    }

    [Fact]
    public void ObservedIdleFurnace_NoOperations()
    {
        var (input, _) = _rig.PlaceFurnace();
        _rig.Stock(input, 0, CraftingServerRig.Ore, 4);
        Observe(input);
        var journaled = _rig.Journal.Entries.Count;

        // No recipe chosen: the furnace has nothing to do however long it is open.
        _rig.Tick(300);

        Assert.Equal(journaled, _rig.Journal.Entries.Count);
        Assert.Equal(0, _rig.Stations.SettlementsApplied);
    }

    [Fact]
    public void Settle_EveryTenTicks_NotEveryTick()
    {
        // The piece is done at tick 65: the service looks at the multiples of 10 only, so it is settled at tick 70, not at 65.
        var (input, output) = SmeltingFurnace(new Tick(5));
        Observe(input);
        var journaled = _rig.Journal.Entries.Count;
        var settledAt = new List<ulong>();

        for (var i = 0; i < 100; i++)
        {
            var before = _rig.Journal.Entries.Count;
            var tick = _rig.Sim.State.CurrentTick.Value;
            _rig.Tick(1);
            if (_rig.Journal.Entries.Count > before)
            {
                settledAt.Add(tick);
            }
        }

        Assert.Equal([70UL], settledAt);
        Assert.Equal(journaled + 1, _rig.Journal.Entries.Count);
        Assert.Equal(1, _rig.CountIn(output, CraftingServerRig.Ingot));
    }

    [Fact]
    public void ReadOnlyServer_NoOperations()
    {
        var (input, output) = SmeltingFurnace(_rig.Sim.State.CurrentTick);
        Observe(input);
        var readOnly = new Horyzont.Server.Crafting.StationService(
            () => _rig.Sim, _rig.Inventory.CollectObservedStations, () => true, Microsoft.Extensions.Logging.Abstractions.NullLogger<Horyzont.Server.Crafting.StationService>.Instance);
        var journaled = _rig.Journal.Entries.Count;

        // Safe mode and shutdown do not change the world (the same rule as for the containers of a joining player).
        for (var i = 0; i < 100; i++)
        {
            readOnly.Step();
            _rig.Sim.AdvanceTick();
        }

        Assert.Equal(journaled, _rig.Journal.Entries.Count);
        Assert.Equal(0, _rig.CountIn(output, CraftingServerRig.Ingot));
    }
}
