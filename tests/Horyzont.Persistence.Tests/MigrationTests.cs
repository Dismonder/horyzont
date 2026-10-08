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
        Assert.Single(loaded.State.Planets.All);
        Assert.NotEmpty(loaded.JournalTail);
        Assert.True(sim.State.Players.Count > 0);
        Assert.Equal(loaded.JournalTail[^1].Id, sim.State.LastOpId);

        var migrated = Load(dir.WorldPath, [MigrationRunner.All[0], new TestMigration2()]);
        Assert.Equal(loaded.JournalTail.Count, migrated.JournalTail.Count);
    }

    [Fact]
    public void TestMigration2_MakesPreMigrationCopy_AndRaisesUserVersion()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);

        Load(path, [MigrationRunner.All[0], new TestMigration2()]);

        Assert.Equal(2L, RawDb.Long(path, "PRAGMA user_version;"));
        Assert.Equal(1L, RawDb.Long(path, "SELECT COUNT(*) FROM test_m2;"));
        var copy = Assert.Single(Directory.GetFiles(dir.BackupsPath));
        Assert.Equal("20261008-093015-000001-pre-migration.db", Path.GetFileName(copy));
        Assert.Equal(1L, RawDb.Long(copy, "PRAGMA user_version;"));
        Assert.Equal("delete", RawDb.Scalar(copy, "PRAGMA journal_mode;"));

        Load(path, [MigrationRunner.All[0], new TestMigration2()]);
        Assert.Single(Directory.GetFiles(dir.BackupsPath));
    }

    [Fact]
    public void FailingMigration_RollsBackAndKeepsVersion()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);

        Assert.Throws<SqliteException>(() => Load(path, [MigrationRunner.All[0], new FailingMigration2()]));

        Assert.Equal(1L, RawDb.Long(path, "PRAGMA user_version;"));
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
        Assert.Equal(1, error.SupportedVersion);
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
        Assert.Equal(1L, RawDb.Long(path, "PRAGMA user_version;"));
    }

    private sealed class TestMigration2 : IMigration
    {
        public int Number => 2;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "CREATE TABLE test_m2 (x INTEGER); INSERT INTO test_m2 VALUES (1);";
            command.ExecuteNonQuery();
        }
    }

    private sealed class FailingMigration2 : IMigration
    {
        public int Number => 2;

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
