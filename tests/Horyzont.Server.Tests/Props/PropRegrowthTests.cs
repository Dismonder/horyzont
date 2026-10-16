using System.Numerics;

using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Server.Props;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Props;

/// <summary>
/// Regrowth of harvested flora as operations of the server (K6-31 D14). Time passes in ticks (the test content regrows a tree after 600 of
/// them); the schedule scans every 60th tick, so a prop that is due at tick 600 regrows at the scan of tick 600. Every refusal test first
/// shows the valid twin regrowing, because a schedule that never regrows would pass them trivially.
/// </summary>
public sealed class PropRegrowthTests
{
    private static PropRegrowth NewRegrowth(PropServerFixture f, bool readOnly = false) =>
        new(() => f.Sim, () => f.Terrain, () => readOnly, NullLogger<PropRegrowth>.Instance);

    private static PropInstance Tree(PropServerFixture f) => f.FindProp(PropKindV1.Pine);

    private static IEnumerable<Operation> Journaled(PropServerFixture f) => f.Journal.Entries.Select(e => e.Operation);

    [Fact]
    public void Regrow_AfterTime_NobodyNear_RegrowOp()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var tree = Tree(f);
        var regrowth = NewRegrowth(f);
        f.Harvest(tree.Id);
        var entries = f.Journal.Entries.Count;

        f.Run(PropServerFixture.RegrowTicks + 1, regrowth.Tick);

        Assert.False(f.IsHarvested(tree.Id));
        var regrow = Assert.Single(f.Journal.Entries.Skip(entries));
        Assert.Equal(new PropRegrowOp(PropServerFixture.Planet, tree.Id), regrow.Operation);
        Assert.Null(regrow.Actor);
    }

    [Fact]
    public void Regrow_BeforeTime_Nothing()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var tree = Tree(f);
        var regrowth = NewRegrowth(f);
        f.Harvest(tree.Id);
        var entries = f.Journal.Entries.Count;

        // The scans of ticks 0 to 599 see a prop that is not due yet.
        f.Run(PropServerFixture.RegrowTicks, regrowth.Tick);

        Assert.True(f.IsHarvested(tree.Id));
        Assert.Equal(entries, f.Journal.Entries.Count);

        // The twin: the scan of tick 600 regrows it.
        f.Run(1, regrowth.Tick);
        Assert.False(f.IsHarvested(tree.Id));
    }

    [Fact]
    public void Regrow_NotWhileReadOnly()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var tree = Tree(f);
        var readOnly = NewRegrowth(f, readOnly: true);
        f.Harvest(tree.Id);

        f.Run(PropServerFixture.RegrowTicks + 120, readOnly.Tick);
        Assert.True(f.IsHarvested(tree.Id));

        f.Run(61, NewRegrowth(f).Tick);
        Assert.False(f.IsHarvested(tree.Id));
    }

    [Fact]
    public void Regrow_PlayerWithin20m_Waits()
    {
        var f = new PropServerFixture();
        var tree = Tree(f);
        var player = PropServerFixture.Player();
        var regrowth = NewRegrowth(f);
        f.Harvest(tree.Id);

        // 15 m from the anchor: the time is up several scans ago, and the tree still waits.
        f.PlaceBeside(player, tree, 15f);
        f.Run(PropServerFixture.RegrowTicks + 300, regrowth.Tick);
        Assert.True(f.IsHarvested(tree.Id));

        // The twin: 25 m away it regrows at the next scan.
        f.PlaceBeside(player, tree, 25f);
        f.Run(61, regrowth.Tick);
        Assert.False(f.IsHarvested(tree.Id));
    }

    [Fact]
    public void Regrow_ConstructWithin20m_Waits()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var regrowth = NewRegrowth(f);
        var flora = f.PropsAround(200f).Where(p => PropServerFixture.Regrows(p.Kind)).ToList();
        var held = flora[0];
        var free = flora.First(p => Vector3.Distance(p.Anchor, held.Anchor) > 80f);
        f.Harvest(held.Id);
        f.Harvest(free.Id);

        // A block 10 m from the first anchor and a block 40 m from the second one, along the surface.
        f.AddConstruct(held.Anchor + (PropFixtures.SideOf(held) * 10f));
        f.AddConstruct(free.Anchor + (PropFixtures.SideOf(free) * 40f));
        f.Run((PropServerFixture.RegrowTicks * 3) + 1, regrowth.Tick);

        // The far block does not hold its tree back (the twin); the near block does.
        Assert.False(f.IsHarvested(free.Id));
        Assert.True(f.IsHarvested(held.Id));
    }

    [Fact]
    public void Regrow_EditedAnchorBlock_Never()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var regrowth = NewRegrowth(f);
        var near = f.PropsAround(200f).Where(p => PropServerFixture.Regrows(p.Kind)).ToList();
        var edited = near[0];
        var untouched = near.First(p => Vector3.Distance(p.Anchor, edited.Anchor) > 60f);
        f.EditTerrainAt(edited.Anchor);
        Assert.NotEqual(0, f.State.TerrainVersions.Count);
        f.Harvest(edited.Id);
        f.Harvest(untouched.Id);

        f.Run((PropServerFixture.RegrowTicks * 3) + 1, regrowth.Tick);

        // The twin regrew; the one whose ground was dug stays harvested for good (no flora grows over a hole).
        Assert.False(f.IsHarvested(untouched.Id));
        Assert.True(f.IsHarvested(edited.Id));
    }

    [Fact]
    public void Regrow_RockNeverRegrows()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var tree = Tree(f);
        var rock = f.FindProp(PropKindV1.Rock);
        var boulder = f.FindProp(PropKindV1.Boulder);
        var crystals = f.FindProp(PropKindV1.Crystals);
        var regrowth = NewRegrowth(f);
        f.Harvest(tree.Id);
        f.Harvest(rock.Id);
        f.Harvest(boulder.Id);
        f.Harvest(crystals.Id);

        f.Run((PropServerFixture.RegrowTicks * 5) + 1, regrowth.Tick);

        Assert.False(f.IsHarvested(tree.Id));
        Assert.True(f.IsHarvested(rock.Id));
        Assert.True(f.IsHarvested(boulder.Id));
        Assert.True(f.IsHarvested(crystals.Id));
    }

    [Fact]
    public void Regrow_ManyDue_BatchedMax64()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var regrowth = NewRegrowth(f);
        var flora = f.PropsAround(500f).Where(p => PropServerFixture.Regrows(p.Kind)).Take(150).ToList();
        Assert.Equal(150, flora.Count);
        f.Sim.ApplyServerOperation(new CompoundOp([.. flora.Select(p => (Operation)new PropHarvestOp(PropServerFixture.Planet, p.Id, f.State.CurrentTick))]));
        var entries = f.Journal.Entries.Count;

        f.Run(PropServerFixture.RegrowTicks + 1, regrowth.Tick);

        // All 150 are due at the same scan: they regrow in operations of at most 64 (64 + 64 + 22), in the order of the identifiers.
        Assert.Equal(0, f.State.HarvestedProps.Count);
        var ops = Journaled(f).Skip(entries).ToList();
        Assert.Equal([64, 64, 22], ops.Select(o => Assert.IsType<CompoundOp>(o).Components.Count));
        Assert.All(ops.SelectMany(o => ((CompoundOp)o).Components), c => Assert.IsType<PropRegrowOp>(c));
        Assert.Equal(
            flora.Select(p => p.Id),
            ops.SelectMany(o => ((CompoundOp)o).Components).Cast<PropRegrowOp>().Select(c => c.Prop));
    }

    [Fact]
    public void Regrow_TwoDue_OneCompound()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var regrowth = NewRegrowth(f);
        var trees = f.PropsAround(300f).Where(p => PropServerFixture.Regrows(p.Kind)).Take(2).ToList();
        f.Harvest(trees[0].Id);
        f.Harvest(trees[1].Id);
        var entries = f.Journal.Entries.Count;

        f.Run(PropServerFixture.RegrowTicks + 1, regrowth.Tick);

        Assert.Equal(0, f.State.HarvestedProps.Count);
        var op = Assert.IsType<CompoundOp>(Assert.Single(Journaled(f).Skip(entries)));
        Assert.Equal(2, op.Components.Count);
    }

    [Fact]
    public void Regrow_CursorWraps_AllVisited()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var regrowth = NewRegrowth(f);
        var all = f.PropsAround(900f);
        var rocks = all.Where(p => !PropServerFixture.Regrows(p.Kind)).ToList();
        var trees = all.Where(p => PropServerFixture.Regrows(p.Kind)).ToList();

        // 600 harvested props in the order of the store: a tree with the lowest identifier, 598 rocks that never regrow, a tree with the highest.
        var low = trees[0];
        var between = rocks.Where(r => r.Id.Value > low.Id.Value).Take(598).ToList();
        Assert.Equal(598, between.Count);
        var high = trees.First(t => t.Id.Value > between[^1].Id.Value);
        List<PropInstance> harvested = [low, .. between, high];
        f.Sim.ApplyServerOperation(new CompoundOp([.. harvested.Select(p => (Operation)new PropHarvestOp(PropServerFixture.Planet, p.Id, f.State.CurrentTick))]));
        Assert.Equal(600, f.State.HarvestedProps.Count);

        // A scan looks at 256 entries and the cursor wraps, so three scans after the time is up visit all 600 (256 x 3 = 768).
        f.Run(PropServerFixture.RegrowTicks, regrowth.Tick);
        Assert.Equal(600, f.State.HarvestedProps.Count);
        f.Run(180 + 1, regrowth.Tick);

        Assert.False(f.IsHarvested(low.Id));
        Assert.False(f.IsHarvested(high.Id));
        Assert.Equal(598, f.State.HarvestedProps.Count);
    }

    [Fact]
    public void Regrow_IdleScan_DoesNotAllocate()
    {
        var f = new PropServerFixture();
        f.Place(PropServerFixture.Player(), PropServerFixture.Antipode);
        var regrowth = NewRegrowth(f);
        var rocks = f.PropsAround(600f).Where(p => !PropServerFixture.Regrows(p.Kind)).Take(300).ToList();
        Assert.Equal(300, rocks.Count);
        f.Sim.ApplyServerOperation(new CompoundOp([.. rocks.Select(p => (Operation)new PropHarvestOp(PropServerFixture.Planet, p.Id, f.State.CurrentTick))]));

        // Warm-up (the JIT, the first lookups), then ten scans that find nothing to regrow: rocks are skipped by their kind alone.
        f.Run(240, regrowth.Tick);
        var allocated = 0L;
        for (var i = 0; i < 600; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            regrowth.Tick();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            f.Sim.AdvanceTick();
        }

        Assert.Equal(0, allocated);
        Assert.Equal(300, f.State.HarvestedProps.Count);
    }
}
