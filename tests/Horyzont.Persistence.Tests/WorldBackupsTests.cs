using Horyzont.Core.Hashing;
using Horyzont.Persistence.Backup;
using Horyzont.Persistence.Tests.Support;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

[Collection(SqlitePools.Name)]
public sealed class WorldBackupsTests
{
    private static readonly DateTimeOffset _start = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RestoreLatest_MovesCorruptAndCopiesNewestPublished()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(_start);
        ulong newestHash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, time: time))
        {
            Assert.True(await h.Writer.BackupAsync("older"));
            h.Join(1);
            await h.FlushAndPump();
            time.Advance(TimeSpan.FromMinutes(5));
            Assert.True(await h.Writer.BackupAsync("newest"));
            newestHash = StateHasher.Hash(h.State);
            h.Join(2);
            await h.FlushAndPump();
        }

        // A copy that is still being written must never be restored, even though its name sorts last.
        File.WriteAllText(Path.Combine(dir.BackupsPath, "20991231-235959-000099-zzz.db.partial"), "half a copy");
        File.WriteAllBytes(dir.WorldPath, new byte[8192]);
        File.WriteAllBytes(dir.WorldPath + "-wal", new byte[64]);
        File.WriteAllBytes(dir.WorldPath + "-shm", new byte[64]);
        time.Advance(TimeSpan.FromMinutes(5));

        var result = WorldBackups.RestoreLatest(dir.WorldPath, dir.BackupsPath, time);

        Assert.NotNull(result);
        Assert.EndsWith("-newest.db", result.RestoredFrom, StringComparison.Ordinal);
        Assert.Equal(dir.WorldPath + ".corrupt-20261009-061000", result.CorruptPath);
        Assert.True(File.Exists(result.CorruptPath));
        Assert.True(File.Exists(result.CorruptPath + "-wal"));
        Assert.True(File.Exists(result.CorruptPath + "-shm"));
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        Assert.False(File.Exists(dir.WorldPath + ".restoring"));
        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(newestHash, StateHasher.Hash(sim.State));
        Assert.Single(loaded.State.Players.All);
    }

    [Fact]
    public void RestoreLatest_NoPublishedBackup_ReturnsNullAndTouchesNothing()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.BackupsPath);
        File.WriteAllText(Path.Combine(dir.BackupsPath, "20261009-060000-000001-startup.db.partial"), "half");
        File.WriteAllBytes(dir.WorldPath, new byte[128]);

        var result = WorldBackups.RestoreLatest(dir.WorldPath, dir.BackupsPath, new FakeTimeProvider(_start));

        Assert.Null(result);
        Assert.Equal(128, new FileInfo(dir.WorldPath).Length);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.corrupt-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RestoreLatest_SameSecondTwice_DoesNotOverwriteTheEarlierCorruptFile()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(_start);
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, time: time))
        {
            Assert.True(await h.Writer.BackupAsync("startup"));
        }

        File.WriteAllBytes(dir.WorldPath, [1, 2, 3]);
        var first = WorldBackups.RestoreLatest(dir.WorldPath, dir.BackupsPath, time);
        File.WriteAllBytes(dir.WorldPath, [4, 5, 6]);
        var second = WorldBackups.RestoreLatest(dir.WorldPath, dir.BackupsPath, time);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.CorruptPath, second.CorruptPath);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(first.CorruptPath));
        Assert.Equal([4, 5, 6], File.ReadAllBytes(second.CorruptPath));
    }

    [Fact]
    public async Task DiscardJournalTail_CopiesFirstThenDropsEntriesAfterTheCheckpoint()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(_start);
        ulong checkpointHash;
        Core.Ids.OpId barrier;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, time: time))
        {
            var p = h.Join(1);
            h.Dig(p, 3f);
            Assert.True(await h.CheckpointAndPump());
            checkpointHash = StateHasher.Hash(h.State);
            barrier = h.State.LastOpId;
            h.Dig(p, -3f);
            h.Leave(p);
            await h.FlushAndPump();
        }

        var before = RawDb.JournalIds(dir.WorldPath);
        Assert.NotEmpty(before);

        var (backup, dropped) = WorldBackups.DiscardJournalTail(dir.WorldPath, barrier, dir.BackupsPath, 10, time);

        Assert.EndsWith("-pre-recover.db", backup, StringComparison.Ordinal);
        Assert.Equal(before.Count, dropped);
        Assert.Empty(RawDb.JournalIds(dir.WorldPath));
        var (loadedCopy, _, _) = WorldHarness.LoadAndReplay(backup, WorldHarness.LoadOptions(backup));
        Assert.Equal(before.Count, loadedCopy.JournalTail.Count);
        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Empty(loaded.JournalTail);
        Assert.Equal(barrier, loaded.LastCheckpointOpId);
        Assert.Equal(checkpointHash, StateHasher.Hash(sim.State));
    }
}
