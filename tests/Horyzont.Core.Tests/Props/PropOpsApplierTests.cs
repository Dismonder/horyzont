using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Props.PropFixture;

namespace Horyzont.Core.Tests.Props;

/// <summary>K6-31 D13: operations 48 (harvest) and 47 (regrow) as standalone operations. Every refusal starts from the valid counterpart.</summary>
public sealed class PropOpsApplierTests
{
    [Fact]
    public void PropHarvest_NotHarvested_Adds()
    {
        var f = new PropFixture();
        var before = StateHasher.Hash(f.State);
        var last = f.State.LastOpId;

        var applied = f.Apply(Harvest(Tree(0), 120));

        Assert.Equal(1, f.State.HarvestedProps.Count);
        Assert.True(f.State.HarvestedProps.TryGetTick(Planet, Tree(0), out var tick));
        Assert.Equal(new Tick(120), tick);
        Assert.Equal(last.Next, f.State.LastOpId);
        Assert.NotEqual(before, StateHasher.Hash(f.State));
        Assert.Equal(Harvest(Tree(0), 120), applied.Operation);
        Assert.Empty(applied.TerrainVersions);
        Assert.Empty(applied.ConstructChanges);
        Assert.Empty(applied.ContainerChanges);
        Assert.Equal(applied.Id, Assert.Single(f.Sim.Journal.Entries, e => e.Operation is PropHarvestOp).Id);
    }

    [Fact]
    public void PropHarvest_AlreadyHarvested_Fails()
    {
        var f = new PropFixture();
        f.Apply(Harvest(Tree(0), 120));

        f.AssertFails(Harvest(Tree(0), 500));

        Assert.True(f.State.HarvestedProps.TryGetTick(Planet, Tree(0), out var tick));
        Assert.Equal(new Tick(120), tick);
        f.Apply(Harvest(Tree(1), 130));
        Assert.Equal(2, f.State.HarvestedProps.Count);
    }

    [Fact]
    public void PropHarvest_UnknownPlanet_Fails()
    {
        var f = new PropFixture();
        f.Apply(Harvest(Tree(0)));

        f.AssertFails(new PropHarvestOp(new PlanetId(99), Tree(1), new Tick(100)));

        Assert.False(f.IsHarvested(Tree(1)));
        Assert.Equal(1, f.State.HarvestedProps.Count);
    }

    [Fact]
    public void PropRegrow_Harvested_Removes()
    {
        var f = new PropFixture();
        f.Apply(Harvest(Tree(0)));
        f.Apply(Harvest(Rock(0)));

        var applied = f.Apply(Regrow(Tree(0)));

        Assert.False(f.IsHarvested(Tree(0)));
        Assert.True(f.IsHarvested(Rock(0)));
        Assert.Equal(1, f.State.HarvestedProps.Count);
        Assert.Equal(Regrow(Tree(0)), applied.Operation);
        Assert.Empty(applied.ContainerChanges);
    }

    [Fact]
    public void PropRegrow_NotHarvested_Fails()
    {
        var f = new PropFixture();
        f.Apply(Harvest(Tree(0)));
        f.Apply(Regrow(Tree(0)));

        // The prop is whole again, so a second regrow has nothing to remove; neither has a prop that was never harvested or a prop of an unknown planet.
        f.AssertFails(Regrow(Tree(0)));
        f.AssertFails(Regrow(Tree(5)));
        f.Apply(Harvest(Tree(5)));
        f.AssertFails(new PropRegrowOp(new PlanetId(99), Tree(5)));
        Assert.True(f.IsHarvested(Tree(5)));
    }

    [Fact]
    public void PropHarvest_DoesNotCallTerrainOrGenerator()
    {
        var terrain = new FakeTerrainService();
        var f = new PropFixture(terrain: terrain);

        f.Apply(Harvest(Tree(0)));
        f.Apply(Harvest(Rock(3)));
        f.Apply(Regrow(Tree(0)));

        Assert.Equal(1, f.State.HarvestedProps.Count);
        Assert.Equal(0, terrain.MeasureCalls);
        Assert.Equal(0, terrain.SolidSamples);
        Assert.Empty(terrain.AppliedEdits);
        Assert.Empty(terrain.BlockingLoads);
        Assert.Empty(terrain.LoadedAreas);
    }

    [Fact]
    public void HarvestedProps_SortedByPlanetThenProp_RegionRangeIsContiguous()
    {
        var second = new PlanetParams(new PlanetId(2), new SystemId(1), 9, "core:test/1");
        var f = new PropFixture(state: WorldFactory.CreateWith(WorldFactory.HomePlanetParams, second));
        PropId[] props = [Tree(9), Raw(0, 3, 3, 0), Rock(2), Tree(0), Raw(5, 100, 100, 1), Tree(1)];
        foreach (var prop in props)
        {
            f.Apply(Harvest(prop));
        }

        f.Apply(new PropHarvestOp(second.Id, Tree(0), new Tick(100)));
        f.Apply(new PropHarvestOp(second.Id, Raw(0, 3, 3, 0), new Tick(100)));

        var all = f.State.HarvestedProps.All;
        Assert.Equal(8, all.Count);
        Assert.Equal(all.OrderBy(r => r.Planet.Value).ThenBy(r => r.Prop.Value), all);
        var region = new List<HarvestedPropRecord>();
        f.State.HarvestedProps.CollectRegion(Planet, Tree(0).Region, region);
        Assert.Equal(new PropId[] { Tree(0), Tree(1), Rock(2), Tree(9) }.OrderBy(p => p.Value), region.Select(r => r.Prop));
        Assert.All(region, r => Assert.Equal(Planet, r.Planet));
        Assert.Equal(region.OrderBy(r => r.Prop.Value), region);
    }
}
