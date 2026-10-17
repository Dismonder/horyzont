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
