using Horyzont.Core.World;
using Horyzont.Persistence.Migrations;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class MigrationTests
{
    public const string FixtureName = "world-v1.db";
    public const string FixtureV3Name = "world-v3.db";

    // Newest production schema; the test migrations come right after it (K5-21 added M0002).
    private static int Latest => MigrationRunner.All.Count;

    private static readonly DateTimeOffset _now = new(2026, 10, 8, 9, 30, 15, TimeSpan.Zero);

    private static string CreatePlanetless(TempDirectory dir)
    {
        var world = WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [], [WorldFactory.StoneBlockKey], ["stone"]);
        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, new FakeTimeProvider());
        return dir.WorldPath;
    }

    private static LoadedWorld Load(string path, IReadOnlyList<IMigration> migrations) =>
        WorldLoader.Load(path, WorldHarness.LoadOptions(path) with { Migrations = migrations }, new FakeTimeProvider(_now), NullLogger.Instance);

    [Fact]
    public void FixtureWorldV1_CopiedToTemp_LoadsAndReplays()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);
        Assert.True(File.Exists(fixture), $"Missing {fixture}; generate it once with FixtureGenerator (Category=Manual) and commit it.");
        using var dir = new TempDirectory();
        File.Copy(fixture, dir.WorldPath);

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(WorldFactory.DefaultWorldId, loaded.State.Id);
        // M0002 backfilled the star system around the only planet of the K1-05 fixture (K5-21 decision 12).
        Assert.Equal(4, loaded.State.Planets.All.Count);
        Assert.NotNull(loaded.State.StarSystem);
        Assert.NotEmpty(loaded.JournalTail);
        Assert.True(sim.State.Players.Count > 0);
        Assert.Equal(loaded.JournalTail[^1].Id, sim.State.LastOpId);

        var migrated = Load(dir.WorldPath, [.. MigrationRunner.All, new TestMigrationNext()]);
        Assert.Equal(loaded.JournalTail.Count, migrated.JournalTail.Count);
    }

    [Fact]
    public void FixtureWorldV1_MigratesToLatest_CreatesSurvivalTables()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);
        using var dir = new TempDirectory();
        File.Copy(fixture, dir.WorldPath);
        Assert.Equal(1L, RawDb.Long(dir.WorldPath, "PRAGMA user_version;"));

        var loaded = WorldLoader.Load(
            dir.WorldPath, WorldHarness.LoadOptions(dir.WorldPath), new FakeTimeProvider(_now), NullLogger.Instance);

        Assert.Equal(Latest + 0L, RawDb.Long(dir.WorldPath, "PRAGMA user_version;"));
        foreach (var table in new[] { "item_types", "containers", "block_entities", "player_progress", "harvested_props" })
        {
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, $"SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = '{table}';"));
            Assert.Equal(0L, RawDb.Long(dir.WorldPath, $"SELECT COUNT(*) FROM {table};"));
        }

        var copy = Assert.Single(Directory.GetFiles(dir.BackupsPath));
        Assert.Equal(1L, RawDb.Long(copy, "PRAGMA user_version;"));
        Assert.Equal(0, loaded.State.Containers.Count);
        Assert.Equal(1UL, loaded.State.ContainerIds.NextValue);
        Assert.Empty(loaded.State.ItemTypes.Entries);
    }

    [Fact]
    public void FixtureWorldV3_CopiedToTemp_LoadsAndReplays()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureV3Name);
        Assert.True(File.Exists(fixture), $"Missing {fixture}; generate it once with FixtureGenerator (Category=Manual) and commit it.");
        using var dir = new TempDirectory();
        File.Copy(fixture, dir.WorldPath);
        Assert.Equal(3L, RawDb.Long(dir.WorldPath, "PRAGMA user_version;"));

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(WorldFactory.DefaultWorldId, loaded.State.Id);
        Assert.NotEmpty(loaded.JournalTail);
        Assert.Equal(SurvivalContentFactory.ItemKeys, sim.State.ItemTypes.Entries.Select(e => e.Key));
        Assert.Equal(3, sim.State.Containers.Count);
        Assert.Equal(4UL, sim.State.ContainerIds.NextValue);
        Assert.True(sim.State.Containers.TryGet(WorldFactory.HotbarOf(1), out var hotbar));
        Assert.Equal(WorldFactory.Item(SurvivalContentFactory.GemKey), hotbar.GetSlot(1).Type);
        Assert.Equal(loaded.JournalTail[^1].Id, sim.State.LastOpId);
        Assert.False(Directory.Exists(dir.BackupsPath) && Directory.GetFiles(dir.BackupsPath).Length > 0);
    }

    [Fact]
    public void TestMigration3_MakesPreMigrationCopy_AndRaisesUserVersion()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);

        Load(path, [.. MigrationRunner.All, new TestMigrationNext()]);

        Assert.Equal(Latest + 1L, RawDb.Long(path, "PRAGMA user_version;"));
        Assert.Equal(1L, RawDb.Long(path, "SELECT COUNT(*) FROM test_m3;"));
        var copy = Assert.Single(Directory.GetFiles(dir.BackupsPath));
        Assert.Equal("20261008-093015-000001-pre-migration.db", Path.GetFileName(copy));
        Assert.Equal(Latest, RawDb.Long(copy, "PRAGMA user_version;"));
        Assert.Equal("delete", RawDb.Scalar(copy, "PRAGMA journal_mode;"));

        Load(path, [.. MigrationRunner.All, new TestMigrationNext()]);
        Assert.Single(Directory.GetFiles(dir.BackupsPath));
    }

    [Fact]
    public void FailingMigration_RollsBackAndKeepsVersion()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);

        Assert.Throws<SqliteException>(() => Load(path, [.. MigrationRunner.All, new FailingMigrationNext()]));

        Assert.Equal(Latest, RawDb.Long(path, "PRAGMA user_version;"));
        Assert.Equal(0L, RawDb.Long(path, "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'half_done';"));
    }

    [Fact]
    public void UserVersion99_ThrowsWorldFromNewerVersion()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);
        RawDb.Execute(path, "PRAGMA user_version = 99;");

        var error = Assert.Throws<WorldFromNewerVersionException>(() => Load(path, MigrationRunner.All));

        Assert.Equal(99, error.SchemaVersion);
        Assert.Equal(Latest, error.SupportedVersion);
    }

    [Fact]
    public void All_NumbersContiguousFromOne()
    {
        MigrationRunner.Validate(MigrationRunner.All);

        Assert.Equal(Enumerable.Range(1, MigrationRunner.All.Count), MigrationRunner.All.Select(m => m.Number));
    }

    [Theory]
    [InlineData(new[] { 1, 3 })]
    [InlineData(new[] { 1, 1 })]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 2, 1 })]
    [InlineData(new int[0])]
    public void ListWithGapOrRepeat_ThrowsAtStart(int[] numbers)
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);
        IReadOnlyList<IMigration> list = numbers.Select(n => n == 1 ? MigrationRunner.All[0] : new NumberedMigration(n)).ToList();

        Assert.Throws<InvalidOperationException>(() => MigrationRunner.Validate(list));
        Assert.Throws<InvalidOperationException>(() => Load(path, list));
        Assert.Equal(Latest, RawDb.Long(path, "PRAGMA user_version;"));
    }

    private sealed class TestMigrationNext : IMigration
    {
        public int Number => Latest + 1;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "CREATE TABLE test_m3 (x INTEGER); INSERT INTO test_m3 VALUES (1);";
            command.ExecuteNonQuery();
        }
    }

    private sealed class FailingMigrationNext : IMigration
    {
        public int Number => Latest + 1;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "CREATE TABLE half_done (x INTEGER); INSERT INTO no_such_table VALUES (1);";
            command.ExecuteNonQuery();
        }
    }

    private sealed class NumberedMigration(int number) : IMigration
    {
        public int Number => number;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
        }
    }
}
