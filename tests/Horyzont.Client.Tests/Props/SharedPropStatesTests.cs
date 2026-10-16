using System.Numerics;

using Horyzont.Client.Props;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Props;

/// <summary>The harvested props of a client that shares the world with the server (solo and host, K6-31 D18).</summary>
public sealed class SharedPropStatesTests
{
    private static readonly PlanetId _planet = WorldFactory.HomePlanet;
    private static readonly PropId _tree = PropId.Create(0, 9, 2, 0);
    private static readonly PropId _bush = PropId.Create(0, 9, 3, 0);

    private static ServerSimulation Simulation() =>
        new(WorldFactory.CreateWithProps(), new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);

    [Fact]
    public void IsHarvested_ReadsTheWorld_BeforeAndAfterTheWorldIsLoaded()
    {
        ServerSimulation? sim = null;
        var states = new SharedPropStates(() => sim);
        Assert.False(states.IsHarvested(_planet, _tree));

        sim = Simulation();
        Assert.False(states.IsHarvested(_planet, _tree));
        sim.ApplyServerOperation(new PropHarvestOp(_planet, _tree, sim.State.CurrentTick));

        Assert.True(states.IsHarvested(_planet, _tree));
        Assert.False(states.IsHarvested(_planet, _bush));
        sim.ApplyServerOperation(new PropRegrowOp(_planet, _tree));
        Assert.False(states.IsHarvested(_planet, _tree));
    }

    [Fact]
    public void RegionChanged_RaisedForHarvestRegrowAndDig_OncePerRegion()
    {
        var sim = Simulation();
        var states = new SharedPropStates(() => sim);
        var raised = new List<(PlanetId Planet, PropRegionKey Region)>();
        states.RegionChanged += (planet, region) => raised.Add((planet, region));

        sim.ApplyServerOperation(new PropHarvestOp(_planet, _tree, sim.State.CurrentTick));
        sim.ApplyServerOperation(new CompoundOp([new PropRegrowOp(_planet, _tree), new PropHarvestOp(_planet, _bush, sim.State.CurrentTick)]));
        sim.ApplyServerOperation(new CompoundOp(
        [
            new PropRegrowOp(_planet, _bush),
            new TerrainEditOp(_planet, TerrainShape.Sphere, new Vector3(0, 4000, 0), 1f, TerrainEditMode.Remove, 0),
        ]));

        Assert.Equal(3, raised.Count);
        Assert.All(raised, entry => Assert.Equal((_planet, _tree.Region), entry));
        Assert.Equal(_tree.Region, _bush.Region);
    }
}
