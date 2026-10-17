using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;
using Horyzont.Server.Tests.Survival;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Crafting;

/// <summary>
/// The station service and the oxygen generator (K6-34 D12, step C4): a generator whose input somebody has open is settled every 10 ticks by a server operation (the burnt cells are destroyed, the start moves);
/// a generator nobody observes is not settled, and its protection still ends exactly when its last cell does, because the coverage is a function of the tick and not of the last settlement. The rig's cell burns
/// 600 ticks (10 seconds at 60 ticks per second).
/// </summary>
public sealed class StationServiceGeneratorTests
{
    private readonly HazardServerRig _rig = new();

    private (ContainerId Input, Tick Started) Generator(ushort queued)
    {
        var started = _rig.Sim.State.CurrentTick;
        var input = _rig.PlaceGenerator(started);
        if (queued > 0)
        {
            _rig.Stock(input, 0, HazardServerRig.Fuel, queued);
        }

        return (input, started);
    }

    private void Observe(ContainerId container)
    {
        _rig.Inventory.OnPlayerReady(_rig.Alice);
        _rig.StandOver(_rig.Alice, 4f);
        _rig.Inventory.Handle(_rig.Alice, new ContainerSubscribe(container));
        _rig.Sent.Clear();
    }

    [Fact]
    public void ObservedGenerator_SettledEvery10Ticks()
    {
        var (input, started) = Generator(queued: 2);
        Observe(input);
        var journaled = _rig.Journal.Entries.Count;
        var per = (int)_rig.TicksPerCell;

        // The first cell burns until tick 600 and the two behind it wait: nothing is written before the cell ends, then the service settles at the next multiple of 10.
        _rig.Tick(per - 1);
        Assert.Equal(journaled, _rig.Journal.Entries.Count);
        _rig.Tick(2);

        Assert.Equal(journaled + 1, _rig.Journal.Entries.Count);
        Assert.Equal(1, _rig.Stations.SettlementsApplied);
        Assert.Equal(1, _rig.CountIn(input, HazardServerRig.Fuel));
        Assert.Equal(started.Value + (ulong)per, _rig.Generator().Started!.Value.Value);

        // The open panel hears of it: a delta of the input.
        var delta = Assert.IsType<ContainerDelta>(Assert.Single(_rig.Sent.Where(s => s.Player == _rig.Alice).Select(s => s.Message)));
        Assert.Equal(input, delta.Container);
    }

    [Fact]
    public void UnobservedGenerator_NotSettled_CoverageStillEnds()
    {
        var (input, started) = Generator(queued: 0);
        var journaled = _rig.Journal.Entries.Count;
        var per = _rig.TicksPerCell;
        var point = new Vector3(5.5f, 4001.5f, 0.5f);
        var fuel = WorldFactory.Item(SurvivalContentFactory.FuelPackKey);
        bool Covered(ulong tick) => OxygenCoverage.IsCovered(_rig.Sim.State, WorldFactory.HomePlanet, point, fuel, per, new Tick(tick), _rig.Sim.HazardRules);

        _rig.Tick((int)per + 30);

        // Nobody has the input open, so no operation was written, yet the generator covers the point until the tick its only cell ends and not a tick longer.
        Assert.Equal(journaled, _rig.Journal.Entries.Count);
        Assert.Equal(0, _rig.Stations.SettlementsApplied);
        Assert.Equal(started, _rig.Generator().Started);
        Assert.Equal(0, _rig.CountIn(input, fuel));
        Assert.True(Covered(started.Value + per - 1));
        Assert.False(Covered(started.Value + per));
    }
}
