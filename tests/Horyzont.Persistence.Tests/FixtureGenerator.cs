using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>
/// Writes <c>Fixtures/world-v1.db</c> into the source tree. Run once by hand when schema version 1 is final and commit the file;
/// never regenerate it afterwards, because it stands for worlds saved by released versions.
/// </summary>
public sealed class FixtureGenerator
{
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Generate_WorldV1()
    {
        var target = Path.Combine(RepoRoot(), "tests", "Horyzont.Persistence.Tests", "Fixtures", MigrationTests.FixtureName);
        Assert.False(File.Exists(target), $"{target} exists; delete it first if it really must be regenerated.");
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, time: time))
        {
            var script = new WorldScript(2026);
            for (var i = 0; i < 120; i++)
            {
                script.Step(h.Sim);
            }

            Assert.True(await h.CheckpointAndPump());
            for (var i = 0; i < 30; i++)
            {
                script.Step(h.Sim);
            }

            await h.FlushAndPump();
        }

        // Fold the WAL into the main file (the repository ignores *-wal files); pooled terrain readers must close first.
        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "PRAGMA journal_mode = DELETE;");
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        File.Copy(dir.WorldPath, target);
    }

    /// <summary>
    /// Writes <c>Fixtures/world-v3.db</c> (schema version 3, K6-29W D1): a world with item types, the three containers of a player,
    /// start-kit items in the checkpoint and an item transfer in the journal tail. Same rules as <see cref="Generate_WorldV1"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Generate_WorldV3()
    {
        var target = Path.Combine(RepoRoot(), "tests", "Horyzont.Persistence.Tests", "Fixtures", MigrationTests.FixtureV3Name);
        Assert.False(File.Exists(target), $"{target} exists; delete it first if it really must be regenerated.");
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        var world = WorldState.CreateNew(
            WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey], ["stone", "dirt"],
            SurvivalContentFactory.ItemKeys);
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: world, time: time))
        {
            var player = h.Join(33);
            h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(player, 1));
            var tool = WorldFactory.Item(SurvivalContentFactory.ToolKey);
            var gem = WorldFactory.Item(SurvivalContentFactory.GemKey);
            h.Sim.ApplyServerOperation(new CompoundOp(
            [
                new ItemCreateOp(new SlotRef(WorldFactory.HotbarOf(1), 0), tool, 1, 1, ItemSource.Starter),
                new ItemCreateOp(new SlotRef(WorldFactory.HotbarOf(1), 1), gem, 3, 5, ItemSource.Starter),
            ]));
            var script = new WorldScript(2027);
            for (var i = 0; i < 40; i++)
            {
                script.Step(h.Sim);
            }

            Assert.True(await h.CheckpointAndPump());
            for (var i = 0; i < 20; i++)
            {
                script.Step(h.Sim);
            }

            h.Sim.ApplyServerOperation(new ItemTransferOp(new SlotRef(WorldFactory.HotbarOf(1), 1), new SlotRef(WorldFactory.BagOf(1), 0), gem, 1, 5));
            await h.FlushAndPump();
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "PRAGMA journal_mode = DELETE;");
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        File.Copy(dir.WorldPath, target);
    }

    /// <summary>
    /// Writes <c>Fixtures/world-v3-blocks.db</c> (schema version 3, K6-30 D25): a chest with items, an owned bed with its respawn point, a
    /// lit lamp and an open door in the checkpoint; a door closed by one compound of two function states and an item moved from the bag
    /// into the chest in the journal tail. Same rules as <see cref="Generate_WorldV1"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Generate_WorldV3Blocks()
    {
        var target = Path.Combine(RepoRoot(), "tests", "Horyzont.Persistence.Tests", "Fixtures", MigrationTests.FixtureV3BlocksName);
        Assert.False(File.Exists(target), $"{target} exists; delete it first if it really must be regenerated.");
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FunctionalWorldScript.NewWorld(), time: time))
        {
            var built = FunctionalWorldScript.Build(h);
            var gem = WorldFactory.Item(SurvivalContentFactory.GemKey);
            h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.BagOf(1), 0), gem, 3, 5, ItemSource.Starter));
            Assert.True(await h.CheckpointAndPump());

            h.Sim.ApplyServerOperation(new CompoundOp(
            [
                new BlockFunctionStateSetOp(built.Construct, FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 0),
                new BlockFunctionStateSetOp(built.Construct, FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 0),
            ]));
            h.Sim.ApplyServerOperation(new ItemTransferOp(new SlotRef(WorldFactory.BagOf(1), 0), new SlotRef(built.Chest, 6), gem, 1, 5));
            await h.FlushAndPump();
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "PRAGMA journal_mode = DELETE;");
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        File.Copy(dir.WorldPath, target);
    }

    /// <summary>
    /// Writes <c>Fixtures/world-v3-props.db</c> (schema version 3, K6-31 D23): a planet with the prop layout; a tree, a rock and a bush
    /// harvested in the checkpoint; a tree of another face harvested and the first tree regrown in the journal tail. Same rules as
    /// <see cref="Generate_WorldV1"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Generate_WorldV3Props()
    {
        var target = Path.Combine(RepoRoot(), "tests", "Horyzont.Persistence.Tests", "Fixtures", MigrationTests.FixtureV3PropsName);
        Assert.False(File.Exists(target), $"{target} exists; delete it first if it really must be regenerated.");
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 14, 0, 0, TimeSpan.Zero));
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: PropWorldScript.NewWorld(), time: time))
        {
            await PropWorldScript.BuildAsync(h);
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "PRAGMA journal_mode = DELETE;");
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        File.Copy(dir.WorldPath, target);
    }

    /// <summary>
    /// Writes <c>Fixtures/world-v3-crafting.db</c> (schema version 3, K6-32 D25): two players (Alice with a respawn point and discoveries, Bob with
    /// discoveries only), two chests, a furnace smelting with its input and the progress key <c>test:boss_2</c> in the checkpoint; the progress key
    /// <c>test:boss</c>, a discovery, the settlement of one piece of the furnace and a craft from two chests in the journal tail. Same rules as
    /// <see cref="Generate_WorldV1"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Generate_WorldV3Crafting()
    {
        var target = Path.Combine(RepoRoot(), "tests", "Horyzont.Persistence.Tests", "Fixtures", MigrationTests.FixtureV3CraftingName);
        Assert.False(File.Exists(target), $"{target} exists; delete it first if it really must be regenerated.");
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: CraftingWorldScript.NewWorld(), time: time))
        {
            var built = CraftingWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());

            h.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:boss"));
            h.Sim.ApplyServerOperation(new ItemDiscoverOp(built.Bob, CraftingWorldScript.Ingot));
            Assert.True(h.State.BlockEntities.TryGet(new Core.BlockEntities.BlockRef(built.Construct, CraftingWorldBuilt.FurnaceCell), out var furnace));
            var station = Assert.IsType<Core.BlockEntities.StationBlockState>(furnace);
            h.Sim.ApplyServerOperation(new CompoundOp(
            [
                new ItemDestroyOp(new SlotRef(built.Input, 0), CraftingWorldScript.Ore, 2, ItemSink.Station),
                new ItemDestroyOp(new SlotRef(built.Input, 1), CraftingWorldScript.Peat, 1, ItemSink.Fuel),
                new ItemCreateOp(new SlotRef(built.Output, 0), CraftingWorldScript.Ingot, 1, 5, ItemSource.Station),
                new BlockEntitySetOp(
                    built.Construct, CraftingWorldBuilt.FurnaceCell, CraftingBlocks.FurnaceType,
                    station with { Started = new Core.Ids.Tick(station.Started!.Value.Value + 60) }),
            ]));
            h.Sim.ApplyServerOperation(new CompoundOp(
            [
                new ItemDestroyOp(new SlotRef(built.Chest, 1), CraftingWorldScript.Stone, 1, ItemSink.Craft),
                new ItemDestroyOp(new SlotRef(built.Chest2, 0), CraftingWorldScript.Stone, 1, ItemSink.Craft),
                new ItemCreateOp(new SlotRef(WorldFactory.BagOf(1), 0), CraftingWorldScript.Brick, 1, 500, ItemSource.Craft),
                new ItemDiscoverOp(built.Alice, CraftingWorldScript.Brick),
            ]));
            await h.FlushAndPump();
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "PRAGMA journal_mode = DELETE;");
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        File.Copy(dir.WorldPath, target);
    }

    /// <summary>
    /// Writes <c>Fixtures/world-v3-food.db</c> (schema version 3, K6-33 D19): Alice online with a bed, discoveries, two meals and her vitals, Bob offline with one meal
    /// (a record without a bed and without discoveries) and his vitals, in the checkpoint; the broth eaten into the third slot (two operations) in the journal tail.
    /// Same rules as <see cref="Generate_WorldV1"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Generate_WorldV3Food()
    {
        var target = Path.Combine(RepoRoot(), "tests", "Horyzont.Persistence.Tests", "Fixtures", FoodPersistenceTests.FixtureName);
        Assert.False(File.Exists(target), $"{target} exists; delete it first if it really must be regenerated.");
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 16, 0, 0, TimeSpan.Zero));
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FoodWorldScript.NewWorld(), time: time))
        {
            var built = FoodWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            FoodWorldScript.BuildTail(h, built);
            await h.FlushAndPump();
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "PRAGMA journal_mode = DELETE;");
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        File.Copy(dir.WorldPath, target);
    }

    /// <summary>
    /// Generates <c>world-v3-hazards.db</c> (K6-34 D21): Alice online with a tank in the module slot and a suit (blob 2 version 3), Bob offline without a suit, one generator that burns with two fuel packs and an idle one,
    /// a checkpoint, and in the journal tail a refill and the settlement of the burning generator. Run once, by hand, and commit the file.
    /// </summary>
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Generate_WorldV3Hazards()
    {
        var target = Path.Combine(RepoRoot(), "tests", "Horyzont.Persistence.Tests", "Fixtures", HazardPersistenceTests.FixtureName);
        Assert.False(File.Exists(target), $"{target} exists; delete it first if it really must be regenerated.");
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: HazardWorldScript.NewWorld(), time: time))
        {
            var built = HazardWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            HazardWorldScript.BuildTail(h, built);
            await h.FlushAndPump();
            Console.WriteLine("HAZARD HASH " + Horyzont.Core.Hashing.StateHasher.Hash(h.State));
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "PRAGMA journal_mode = DELETE;");
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        File.Copy(dir.WorldPath, target);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Horyzont.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
