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
        Assert.Equal("20261008-060000-000001-startup.db", Path.GetFileName(copy));
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
        Assert.Equal("20261008-060200-000003-periodic.db", names[0]);
        Assert.Equal("20261008-061100-000012-periodic.db", names[^1]);
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
            ["20261008-120000-000001-periodic.db", "20261008-180000-000002-periodic.db"],
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
        Assert.EndsWith("-000003-manual.db", third, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedReasons_SameSecond_KeepOne_KeepsTheNewCopy()
    {
        using var dir = new TempDirectory();
        var (source, service, _) = Setup(dir, keep: 1);
        using (source)
        {
            service.CreateBackup(source, "startup");
            var preMigration = service.CreateBackup(source, "pre-migration");

            Assert.True(File.Exists(preMigration));
            Assert.Equal([preMigration], service.List());
        }
    }

    [Fact]
    public void SameReason_AfterRotation_NewCopyIsNewest()
    {
        using var dir = new TempDirectory();
        var (source, service, _) = Setup(dir, keep: 2);
        using (source)
        {
            var copies = Enumerable.Range(0, 5).Select(_ => service.CreateBackup(source, "manual")).ToList();

            Assert.All(copies.TakeLast(2), copy => Assert.True(File.Exists(copy)));
            Assert.Equal(copies.TakeLast(2), service.List());
            Assert.Equal(copies.Count, copies.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void MoreThanNineCopies_SameSecond_KeptInCreationOrder()
    {
        using var dir = new TempDirectory();
        var (source, service, _) = Setup(dir, keep: 3);
        using (source)
        {
            var copies = new List<string>();
            for (var i = 0; i < 12; i++)
            {
                copies.Add(service.CreateBackup(source, i % 2 == 0 ? "periodic" : "manual"));
                if (i == 6)
                {
                    // A clock set back must not reorder the copies either.
                    service = new BackupService(dir.BackupsPath, 3, new FakeTimeProvider(_start.AddHours(-1)));
                }
            }

            Assert.Equal(copies.TakeLast(3), service.List());
        }
    }

    [Fact]
    public void InterruptedCopy_IsNeverAPublishedCopy()
    {
        using var dir = new TempDirectory();
        var (source, service, _) = Setup(dir, keep: 2);
        using (source)
        {
            var first = service.CreateBackup(source, "startup");
            IReadOnlyList<string>? listedDuringCopy = null;
            string[]? filesDuringCopy = null;
            service.BeforePublish = _ =>
            {
                listedDuringCopy = service.List();
                filesDuringCopy = Directory.GetFiles(dir.BackupsPath, "*.db");
                throw new IOException("Simulated death during the copy.");
            };

            Assert.Throws<IOException>(() => service.CreateBackup(source, "periodic"));

            Assert.Equal([first], listedDuringCopy!);
            Assert.Equal([first], filesDuringCopy!);
            Assert.Equal([first], Directory.GetFiles(dir.BackupsPath));
        }
    }

    [Fact]
    public void StaleTemporaryCopy_IsNotListed_AndIsRemovedByTheNextCopy()
    {
        using var dir = new TempDirectory();
        var (source, service, _) = Setup(dir, keep: 2);
        using (source)
        {
            var first = service.CreateBackup(source, "startup");
            var stale = Path.Combine(dir.BackupsPath, "20261008-060000-000099-periodic.db" + BackupService.TemporarySuffix);
            File.WriteAllBytes(stale, [1, 2, 3]);

            Assert.Equal([first], service.List());

            var second = service.CreateBackup(source, "periodic");
            Assert.False(File.Exists(stale));
            Assert.Equal([first, second], service.List());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad reason")]
    [InlineData("pre_migration")]
    [InlineData("zażółć")]
    public async Task BackupAsync_InvalidReason_ThrowsAtCaller_WriterStaysHealthy(string reason)
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath, time: new FakeTimeProvider(_start));
        h.Join(1);

        Assert.ThrowsAny<ArgumentException>(() => { _ = h.Writer.BackupAsync(reason); });

        h.Ticks(1);
        h.Join(2);
        Assert.True(await h.Writer.FlushAsync());
        h.Pump();
        Assert.False(h.Writer.IsFaulted);
        Assert.Equal(h.State.LastOpId, h.Writer.DurableOpId);
        Assert.False(Directory.Exists(dir.BackupsPath) && Directory.GetFiles(dir.BackupsPath).Length > 0);
    }

    [Fact]
    public async Task BackupAsync_CopyThrowsAnyException_FalseWithoutFault()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath, time: new FakeTimeProvider(_start));
        h.Writer.Backups.BeforePublish = _ => throw new InvalidOperationException("copy bug");

        Assert.False(await h.Writer.BackupAsync("startup"));

        Assert.False(h.Writer.IsFaulted);
        h.Join(1);
        Assert.True(await h.Writer.FlushAsync());
        Assert.Equal(h.State.LastOpId, h.Writer.DurableOpId);
        h.Writer.Backups.BeforePublish = null;
        Assert.True(await h.Writer.BackupAsync("startup"));
        Assert.Single(Directory.GetFiles(dir.BackupsPath));
    }

    /// <summary>
    /// E2: a copy held by a gate right before it starts does not hold the journal (JournalCommitted arrives while it waits), and the
    /// finished copy is an intact world whose journal is a prefix of the live one.
    /// </summary>
    [Fact]
    public async Task BackupAsync_CopyInProgress_JournalKeepsCommitting_CopyIsConsistentPrefix()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath, time: new FakeTimeProvider(_start));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        h.Writer.Backups.BeforeCopy = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
        };
        h.Join(1);

        var backup = h.Writer.BackupAsync("startup");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        var script = new WorldScript(5);
        for (var i = 0; i < 15; i++)
        {
            script.Step(h.Sim);
        }

        h.Ticks(1);
        h.Join(2);
        Assert.True(await h.Writer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        h.Pump();

        Assert.False(backup.IsCompleted);
        Assert.Contains(h.Events, e => e is JournalCommitted committed && committed.UpTo == h.State.LastOpId);
        release.Set();
        Assert.True(await backup.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        h.Ticks(1);
        Assert.True(await h.Writer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        var copy = Assert.Single(Directory.GetFiles(dir.BackupsPath));
        Assert.Equal("ok", RawDb.Scalar(copy, "PRAGMA integrity_check;"));
        var (loaded, _, _) = WorldHarness.LoadAndReplay(copy, WorldHarness.LoadOptions(copy));
        Assert.True(loaded.State.Players.TryGet(h.State.Players.Online.First().Player, out _));
        var live = JournalRows(dir.WorldPath);
        var copied = JournalRows(copy);
        Assert.NotEmpty(copied);
        Assert.True(copied.Count <= live.Count);
        Assert.Equal(live.Take(copied.Count).Select(r => r.Key), copied.Select(r => r.Key));
        foreach (var (opId, row) in copied)
        {
            Assert.Equal(live.Single(r => r.Key == opId).Value, row);
        }
    }

    [Fact]
    public async Task BackupAsync_SecondRequestWhileOneIsHeld_DoesNotCopyConcurrently()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath, time: new FakeTimeProvider(_start));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var running = 0;
        var maxRunning = 0;
        var started = 0;
        h.Writer.Backups.BeforeCopy = () =>
        {
            var now = Interlocked.Increment(ref running);
            int seen;
            while ((seen = Volatile.Read(ref maxRunning)) < now && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen)
            {
            }

            if (Interlocked.Increment(ref started) == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
            }
        };
        h.Writer.Backups.BeforePublish = _ => Interlocked.Decrement(ref running);
        h.Join(1);

        var first = h.Writer.BackupAsync("a");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        var second = h.Writer.BackupAsync("b");
        h.Ticks(1);
        Assert.True(await h.Writer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref started));
        Assert.False(second.IsCompleted);
        release.Set();
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Equal(2, started);
        Assert.Equal(1, maxRunning);
        Assert.Equal(0, running);
        Assert.Equal(2, Directory.GetFiles(dir.BackupsPath).Length);
    }

    [Fact]
    public async Task BackupAsync_TwoRequests_RunOneAfterTheOther()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath, time: new FakeTimeProvider(_start));
        var running = 0;
        var overlap = false;
        h.Writer.Backups.BeforeCopy = () => overlap |= Interlocked.Increment(ref running) > 1;
        h.Writer.Backups.BeforePublish = _ =>
        {
            Thread.Yield();
            Interlocked.Decrement(ref running);
        };

        var results = await Task.WhenAll(h.Writer.BackupAsync("a"), h.Writer.BackupAsync("b"), h.Writer.BackupAsync("c"));

        Assert.All(results, Assert.True);
        Assert.False(overlap);
        Assert.Equal(
            ["000001-a.db", "000002-b.db", "000003-c.db"],
            Directory.GetFiles(dir.BackupsPath).Select(f => Path.GetFileName(f)[16..]).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>Every journal row by op_id, all columns as text (payload as hex).</summary>
    private static List<KeyValuePair<long, string>> JournalRows(string path)
    {
        using var connection = RawDb.Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT op_id, tick || '/' || kind || '/' || ifnull(actor, 'null') || '/' || hex(payload) || '/' || crc FROM journal ORDER BY op_id;";
        using var reader = command.ExecuteReader();
        var rows = new List<KeyValuePair<long, string>>();
        while (reader.Read())
        {
            rows.Add(new(reader.GetInt64(0), reader.GetString(1)));
        }

        return rows;
    }

    private static (Microsoft.Data.Sqlite.SqliteConnection Source, BackupService Service, FakeTimeProvider Time) Setup(TempDirectory dir, int keep)
    {
        var time = new FakeTimeProvider(_start);
        WorldCreator.Create(dir.WorldPath, WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [], [], []), WorldHarness.Options, time);
        return (RawDb.Open(dir.WorldPath), new BackupService(dir.BackupsPath, keep, time), time);
    }
}
