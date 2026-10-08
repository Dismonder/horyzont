using Horyzont.Core.Hashing;
using Horyzont.Core.World;
using Horyzont.Persistence.Backup;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class BackupTests
{
    private static readonly DateTimeOffset _start = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Backup_OpensAsValidWorld_WithJournalUpToTheCopy()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath, time: new FakeTimeProvider(_start));
        var script = new WorldScript(11);
        for (var i = 0; i < 60; i++)
        {
            script.Step(h.Sim);
        }

        Assert.True(await h.CheckpointAndPump());
        for (var i = 0; i < 20; i++)
        {
            script.Step(h.Sim);
        }

        var hash = StateHasher.Hash(h.State);

        Assert.True(await h.Writer.BackupAsync("startup"));

        var copy = Assert.Single(Directory.GetFiles(dir.BackupsPath));
        Assert.Equal("20261008-060000-startup.db", Path.GetFileName(copy));
        Assert.False(File.Exists(copy + "-wal"));
        var (loaded, sim, _) = WorldHarness.LoadAndReplay(copy, WorldHarness.LoadOptions(copy));
        Assert.Equal(h.State.Id, loaded.State.Id);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task WriterBackups_Rotation_KeepsTenNewest()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(_start);
        WorldCreator.Create(dir.WorldPath, WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [], [], []), WorldHarness.Options, time);
        await using var writer = new PersistenceWriter(
            WorldDatabase.Open(dir.WorldPath), WorldHarness.Options, time, Faults.NoFaults.Instance, NullLogger<PersistenceWriter>.Instance);

        for (var i = 0; i < 12; i++)
        {
            Assert.True(await writer.BackupAsync("periodic"));
            time.Advance(TimeSpan.FromMinutes(1));
        }

        var names = Directory.GetFiles(dir.BackupsPath).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(10, names.Count);
        Assert.Equal("20261008-060200-periodic.db", names[0]);
        Assert.Equal("20261008-061100-periodic.db", names[^1]);
    }

    [Fact]
    public async Task PeriodicTimer_EverySixHours_MakesBackup()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(_start);
        WorldCreator.Create(dir.WorldPath, WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [], [], []), WorldHarness.Options, time);
        await using var writer = new PersistenceWriter(
            WorldDatabase.Open(dir.WorldPath), new PersistenceOptions { GameVersion = "t" }, time, Faults.NoFaults.Instance,
            NullLogger<PersistenceWriter>.Instance);

        time.Advance(TimeSpan.FromHours(6));
        await writer.ProbeAsync();
        time.Advance(TimeSpan.FromHours(6));
        await writer.ProbeAsync();

        Assert.Equal(
            ["20261008-120000-periodic.db", "20261008-180000-periodic.db"],
            Directory.GetFiles(dir.BackupsPath).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void BackupService_SameSecond_KeepsBothCopiesInOrder()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(_start);
        WorldCreator.Create(dir.WorldPath, WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [], [], []), WorldHarness.Options, time);
        using var source = RawDb.Open(dir.WorldPath);
        var service = new BackupService(dir.BackupsPath, 2, time);

        var first = service.CreateBackup(source, "manual");
        var second = service.CreateBackup(source, "manual");
        var third = service.CreateBackup(source, "manual");

        Assert.Equal([second, third], service.List());
        Assert.False(File.Exists(first));
        Assert.EndsWith("-manual-3.db", third, StringComparison.Ordinal);
    }
}
