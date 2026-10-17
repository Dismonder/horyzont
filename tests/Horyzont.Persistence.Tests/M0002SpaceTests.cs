using Horyzont.Core.Hashing;
using Horyzont.Core.Serialization;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Persistence.Migrations;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>M0002_Space (K5-21 decision 12): three tables and the star system backfill of an existing world.</summary>
public sealed class M0002SpaceTests
{
    private const string Profile = "core:test/1";
    private const ulong Seed = 0x5EED;

    [Fact]
    public void M0002_FreshFile_CreatesThreeTables()
    {
        using var dir = new TempDirectory();
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());

        Assert.Equal((long)MigrationRunner.All.Count, RawDb.Long(dir.WorldPath, "PRAGMA user_version;"));
        Assert.Equal("system_id,params", Columns(dir.WorldPath, "star_systems"));
        Assert.Equal("marker_id,data", Columns(dir.WorldPath, "markers"));
        Assert.Equal("construct_id,data", Columns(dir.WorldPath, "ship_state"));
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM star_systems;"));
        Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM planets;"));
        Assert.Null(Load(dir.WorldPath).State.StarSystem);
    }

    [Fact]
    public void M0002_ExistingWorld_BackfillsSystemAndThreeBodies()
    {
        using var dir = new TempDirectory();
        var home = PlanetGenesis.CreatePlanet(Seed, PlanetGenesis.HomePlanetIndex, Profile);
        CreateM0001World(dir.WorldPath, Seed, home);

        var loaded = Load(dir.WorldPath);

        Assert.Equal((long)MigrationRunner.All.Count, RawDb.Long(dir.WorldPath, "PRAGMA user_version;"));
        Assert.Equal(4L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM planets;"));
        Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM star_systems WHERE system_id = 1;"));
        Assert.Equal(4, loaded.State.Planets.All.Count);
        Assert.NotNull(loaded.State.StarSystem);
        Assert.Equal(home.Id, loaded.State.HomePlanet);
        Assert.Equal(home, loaded.State.Planets.All.Single(p => p.Id == home.Id));
        var copy = Assert.Single(Directory.GetFiles(Path.Combine(dir.Path, "backups")));
        Assert.EndsWith("pre-migration.db", copy, StringComparison.Ordinal);
        Assert.Equal(1L, RawDb.Long(copy, "PRAGMA user_version;"));
    }

    [Fact]
    public void Backfill_EqualsFreshWorldOfSameSeed()
    {
        using var dir = new TempDirectory();
        var old = Path.Combine(dir.Path, "old.db");
        var fresh = Path.Combine(dir.Path, "fresh.db");
        var created = StarSystemGenesis.CreateWorldBodies(Seed, Profile);
        CreateM0001World(old, Seed, created.Bodies[0]);
        WorldCreator.Create(
            fresh,
            WorldState.CreateNew(WorldFactory.DefaultWorldId, Seed, created.System, created.Bodies, [WorldFactory.StoneBlockKey], ["stone", "dirt"]),
            WorldHarness.Options,
            new FakeTimeProvider());

        var backfilled = Load(old).State;
        var direct = Load(fresh).State;

        Assert.Equal(created.System, backfilled.StarSystem);
        Assert.Equal(created.Bodies.OrderBy(p => p.Id.Value), backfilled.Planets.All);
        Assert.Equal(StateHasher.Hash(direct), StateHasher.Hash(backfilled));
        Assert.Equal(
            StateCodec.EncodeStarSystemBlob(created.System),
            (byte[])RawDb.Scalar(old, "SELECT params FROM star_systems;")!);
    }

    [Fact]
    public void M0002_WorldWithTwoPlanets_NoBackfillLogged()
    {
        using var dir = new TempDirectory();
        var home = PlanetGenesis.CreatePlanet(Seed, PlanetGenesis.HomePlanetIndex, Profile);
        var second = PlanetGenesis.CreatePlanet(Seed, 7, Profile);
        CreateM0001World(dir.WorldPath, Seed, home, second);
        var logger = new RecordingLogger();

        var loaded = WorldLoader.Load(dir.WorldPath, WorldHarness.LoadOptions(dir.WorldPath), new FakeTimeProvider(), logger);

        Assert.Null(loaded.State.StarSystem);
        Assert.Equal(2, loaded.State.Planets.All.Count);
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM star_systems;"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.EventId == 103);
    }

    [Fact]
    public void CreateAndLoad_WithStarSystem_HashEqual()
    {
        using var dir = new TempDirectory();
        var world = WorldFactory.CreateWithStarSystem(Seed);
        var expected = StateHasher.Hash(world);
        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, new FakeTimeProvider());

        var loaded = Load(dir.WorldPath).State;

        Assert.Equal(expected, StateHasher.Hash(loaded));
        Assert.Equal(world.StarSystem, loaded.StarSystem);
        Assert.Equal(4L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM planets;"));
    }

    [Fact]
    public void Load_TwoStarSystemRows_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateWithStarSystem(Seed), WorldHarness.Options, new FakeTimeProvider());
        RawDb.Execute(dir.WorldPath, "INSERT INTO star_systems (system_id, params) SELECT 2, params FROM star_systems;");

        Assert.Throws<WorldCorruptedException>(() => Load(dir.WorldPath));
    }

    [Fact]
    public void Load_DamagedStarSystemBlob_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateWithStarSystem(Seed), WorldHarness.Options, new FakeTimeProvider());
        RawDb.Execute(dir.WorldPath, "UPDATE star_systems SET params = $p;", ("$p", new byte[] { 10, 1, 1 }));

        Assert.Throws<WorldCorruptedException>(() => Load(dir.WorldPath));
    }

    private static LoadedWorld Load(string path) =>
        WorldLoader.Load(path, WorldHarness.LoadOptions(path), new FakeTimeProvider(), NullLogger.Instance);

    /// <summary>A world file as K1-05 wrote it: created at the current schema, then the tables of M0002 and M0003 dropped and user_version 1.</summary>
    private static void CreateM0001World(string path, ulong seed, params PlanetParams[] planets)
    {
        var world = WorldState.CreateNew(WorldFactory.DefaultWorldId, seed, planets, [WorldFactory.StoneBlockKey], ["stone", "dirt"]);
        WorldCreator.Create(path, world, WorldHarness.Options, new FakeTimeProvider());
        RawDb.Execute(path, "DROP TABLE star_systems; DROP TABLE markers; DROP TABLE ship_state; DROP TABLE item_types; DROP TABLE containers; DROP TABLE block_entities; "
            + "DROP TABLE player_progress; DROP TABLE harvested_props; DELETE FROM meta WHERE key = 'next_container_id'; PRAGMA user_version = 1;");
        Assert.Equal(1, MigrationRunner.All[0].Number);
    }

    private static string Columns(string path, string table)
    {
        using var connection = RawDb.Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return string.Join(',', names);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, int EventId)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId.Id));
    }
}
