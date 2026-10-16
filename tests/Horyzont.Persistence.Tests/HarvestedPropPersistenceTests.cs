using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>K6-31 stage C: the harvested props go to <c>harvested_props</c> (a table of M0003, columns, no blob) and come back.</summary>
public sealed class HarvestedPropPersistenceTests
{
    private static readonly PlanetId _planet = WorldFactory.HomePlanet;

    // Flora (slot 0) and rocks (slot 1) of different regions and faces.
    private static readonly PropId _tree = PropId.Create(0, 9, 2, 0);
    private static readonly PropId _rock = PropId.Create(0, 9, 2, 1);
    private static readonly PropId _otherFace = PropId.Create(3, 700, 12, 0);

    private static WorldState NewWorld() => WorldFactory.CreateWithProps(0);

    private static void Harvest(WorldHarness h, PropId prop, ulong tick) =>
        h.Sim.ApplyServerOperation(new PropHarvestOp(_planet, prop, new Tick(tick)));

    [Fact]
    public async Task Checkpoint_WritesHarvested_LoadRestores()
    {
        using var dir = new TempDirectory();
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: NewWorld()))
        {
            Harvest(h, _tree, 120);
            Harvest(h, _rock, 300);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM harvested_props;"));

            // The tail: one more harvest on another face, replayed on top of the checkpoint.
            Harvest(h, _otherFace, 900);
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        // The loaded state is the one the simulation replays: the two rows of the checkpoint and the harvest of the tail.
        Assert.Single(loaded.JournalTail);
        Assert.Equal(3, sim.State.HarvestedProps.Count);
        Assert.True(sim.State.HarvestedProps.TryGetTick(_planet, _tree, out var treeTick));
        Assert.Equal(new Tick(120), treeTick);
        Assert.True(sim.State.HarvestedProps.TryGetTick(_planet, _rock, out var rockTick));
        Assert.Equal(new Tick(300), rockTick);
        Assert.True(sim.State.HarvestedProps.Contains(_planet, _otherFace));
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task Checkpoint_Regrown_DeletesRow()
    {
        using var dir = new TempDirectory();
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: NewWorld()))
        {
            Harvest(h, _tree, 120);
            Harvest(h, _rock, 300);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM harvested_props;"));

            h.Sim.ApplyServerOperation(new PropRegrowOp(_planet, _tree));
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM harvested_props;"));
            Assert.Empty(RawDb.JournalIds(dir.WorldPath));
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(1, loaded.State.HarvestedProps.Count);
        Assert.False(loaded.State.HarvestedProps.Contains(_planet, _tree));
        Assert.True(loaded.State.HarvestedProps.Contains(_planet, _rock));
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task Checkpoint_HarvestedAgainAfterRegrow_UpdatesTheTick()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: NewWorld()))
        {
            Harvest(h, _tree, 120);
            Assert.True(await h.CheckpointAndPump());
            h.Sim.ApplyServerOperation(new PropRegrowOp(_planet, _tree));
            Harvest(h, _tree, 5000);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM harvested_props;"));
        }

        var (loaded, _, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(loaded.State.HarvestedProps.TryGetTick(_planet, _tree, out var tick));
        Assert.Equal(new Tick(5000), tick);
    }

    [Fact]
    public async Task Tick_AboveLongMax_RoundTrips()
    {
        using var dir = new TempDirectory();
        var big = (ulong)long.MaxValue + 12345UL;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: NewWorld()))
        {
            Harvest(h, _tree, ulong.MaxValue);
            Harvest(h, _rock, big);
            Assert.True(await h.CheckpointAndPump());

            // SQLite holds the INTEGER signed: the value is written bit for bit, not refused.
            Assert.Equal(-1L, RawDb.Long(dir.WorldPath, "SELECT tick FROM harvested_props WHERE prop_id = " + _tree.Value + ";"));
        }

        var (loaded, _, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(loaded.State.HarvestedProps.TryGetTick(_planet, _tree, out var treeTick));
        Assert.Equal(new Tick(ulong.MaxValue), treeTick);
        Assert.True(loaded.State.HarvestedProps.TryGetTick(_planet, _rock, out var rockTick));
        Assert.Equal(new Tick(big), rockTick);
    }

    [Fact]
    public async Task Row_ReservedBits_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: NewWorld()))
        {
            Harvest(h, _tree, 120);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM harvested_props;"));
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE harvested_props SET prop_id = prop_id | 1;");

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }

    [Fact]
    public async Task Row_UnknownPlanet_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: NewWorld()))
        {
            Harvest(h, _tree, 120);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM harvested_props;"));
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE harvested_props SET planet_id = 99;");

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }

    [Fact]
    public async Task Row_CellOutsideThePlanetGrid_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: NewWorld()))
        {
            Harvest(h, _tree, 120);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM harvested_props;"));
        }

        // A valid identifier of a face, but of a cell far beyond the 784 cells per face of a planet of 4000 m.
        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE harvested_props SET prop_id = $id;", ("$id", (long)PropId.Create(0, 5000, 0, 0).Value));

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }
}
