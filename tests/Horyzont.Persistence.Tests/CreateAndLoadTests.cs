using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class CreateAndLoadTests
{
    private static readonly PlanetParams _second = new(new PlanetId(2), new SystemId(1), 99, "core:test/2", 3000f, 0.5f, 7.5f, 800f, 300f, 1200f);

    [Fact]
    public void Load_NewWorld_SameIdSeedRegistriesPlanetsAndGameVersion()
    {
        using var dir = new TempDirectory();
        var world = WorldFactory.CreateWith(WorldFactory.HomePlanetParams, _second);
        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, new FakeTimeProvider());

        var loaded = WorldLoader.Load(dir.WorldPath, WorldHarness.LoadOptions(dir.WorldPath), new FakeTimeProvider(), NullLogger.Instance);

        Assert.Equal(world.Id, loaded.State.Id);
        Assert.Equal(world.GalaxySeed, loaded.State.GalaxySeed);
        Assert.Equal(world.BlockTypes.Entries, loaded.State.BlockTypes.Entries);
        Assert.Equal(world.Materials.Entries, loaded.State.Materials.Entries);
        Assert.Equal(world.Planets.All, loaded.State.Planets.All);
        Assert.Equal(WorldHarness.GameVersion, loaded.GameVersion);
        Assert.Equal(OpId.None, loaded.LastCheckpointOpId);
        Assert.Empty(loaded.JournalTail);
        Assert.Equal(StateHasher.Hash(world), StateHasher.Hash(loaded.State));
    }

    [Fact]
    public void Create_PlanetlessWorld_MetaSchemaAndPragmas()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var world = WorldState.CreateNew(WorldFactory.DefaultWorldId, ulong.MaxValue, [], [WorldFactory.StoneBlockKey], ["stone"]);

        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, time);

        Assert.Equal(1L, RawDb.Long(dir.WorldPath, "PRAGMA user_version;"));
        Assert.Equal("wal", RawDb.Scalar(dir.WorldPath, "PRAGMA journal_mode;"));
        Assert.Equal(-1L, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'galaxy_seed';"));
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'last_checkpoint_op_id';"));
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'last_checkpoint_tick';"));
        Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'next_entity_id';"));
        Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'next_player_id';"));
        Assert.Equal(WorldHarness.GameVersion, RawDb.Scalar(dir.WorldPath, "SELECT value FROM meta WHERE key = 'game_version';"));
        Assert.Equal("2026-10-08T12:00:00.0000000+00:00", RawDb.Scalar(dir.WorldPath, "SELECT value FROM meta WHERE key = 'created_utc';"));
        Assert.Equal(WorldFactory.DefaultWorldId.Value.ToByteArray(), RawDb.Scalar(dir.WorldPath, "SELECT value FROM meta WHERE key = 'world_id';"));

        var loaded = WorldLoader.Load(dir.WorldPath, WorldHarness.LoadOptions(dir.WorldPath), time, NullLogger.Instance);
        Assert.Equal(ulong.MaxValue, loaded.State.GalaxySeed);
        Assert.Equal(world.BlockTypes.Entries, loaded.State.BlockTypes.Entries);
        Assert.Equal(world.Materials.Entries, loaded.State.Materials.Entries);
    }

    [Fact]
    public void Create_ExistingFile_ThrowsAndKeepsFile()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.WorldPath, "keep");

        Assert.Throws<IOException>(() => WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider()));

        Assert.Equal("keep", File.ReadAllText(dir.WorldPath));
    }

    [Fact]
    public void Load_MissingFile_ThrowsFileNotFound()
    {
        using var dir = new TempDirectory();

        Assert.Throws<FileNotFoundException>(
            () => WorldLoader.Load(dir.WorldPath, WorldHarness.LoadOptions(dir.WorldPath), new FakeTimeProvider(), NullLogger.Instance));
    }
}
