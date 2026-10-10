using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests.Terrain;

[Collection(SqlitePools.Name)]
public sealed class ClientTerrainCacheTests
{
    private const string Fingerprint = "build-1/profile-1";
    private const int SqliteFull = 13;
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(30);
    private static readonly PlanetId _otherPlanet = new(99);

    private static TerrainBlockKey Key(int i, PlanetId? planet = null) => new(planet ?? WorldFactory.HomePlanet, 0, i, -250, 7);

    private static byte[] Blob(int seed, int length = 40)
    {
        var blob = new byte[length];
        for (var i = 0; i < blob.Length; i++)
        {
            blob[i] = (byte)(seed * 31 + i);
        }

        return blob;
    }

    private static ClientTerrainCache Open(
        TempDirectory dir, FakeTimeProvider? time = null, string fingerprint = Fingerprint, ListLogger? logger = null) =>
        new(dir.File("cache/terrain.db"), fingerprint, time ?? new FakeTimeProvider(), logger ?? new ListLogger());

    private static async Task CloseCleanly(ClientTerrainCache cache) => Assert.True(await cache.CloseAsync().WaitAsync(_wait, TestContext.Current.CancellationToken));

    private static Dictionary<TerrainBlockKey, uint> Versions(ClientTerrainCache cache, PlanetId planet)
    {
        var buffer = new TerrainBlockVersion[1024];
        var count = cache.CopyVersions(planet, buffer, 0);
        return buffer.Take(count).ToDictionary(v => v.Key, v => v.Version);
    }

    [Fact]
    public async Task Put_NewBlock_IsFoundBeforeTheWriterRuns()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        cache.BeforeBatch = () =>
        {
            entered.Set();
            release.Wait(_wait, TestContext.Current.CancellationToken);
        };

        cache.Put(Key(1), 3, 0xABCD, Blob(1));

        Assert.True(entered.Wait(_wait, TestContext.Current.CancellationToken));
        Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(1), out var version, out var blob));
        Assert.Equal(3u, version);
        Assert.Equal(Blob(1), blob);
        Assert.True(cache.TryGetHash(Key(1), out var hash));
        Assert.Equal(0xABCDul, hash);
        release.Set();
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task TryGetHeld_ReportsVersionAndHashWithoutReadingData()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.Put(Key(1), 3, 0xABCD, Blob(1));

        Assert.True(cache.TryGetHeld(Key(1), out var version, out var hash));
        Assert.Equal((3u, 0xABCDul), (version, hash));
        Assert.False(cache.TryGetHeld(Key(2), out version, out hash));
        Assert.Equal((0u, 0ul), (version, hash));
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task Put_VersionZeroWithData_IsFoundNotNotEdited()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);

        Assert.Equal(TerrainBlockLookup.NotEdited, cache.TryLoad(Key(1), out _, out _));
        cache.Put(Key(1), 0, 5, Blob(1));

        Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(1), out var version, out var blob));
        Assert.Equal(0u, version);
        Assert.Equal(Blob(1), blob);
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task Put_CallerMutatesArrayAfterwards_CachedBlobIsUnchanged()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        var source = Blob(1);

        cache.Put(Key(1), 1, 1, source);
        source[0] ^= 0xFF;

        cache.TryLoad(Key(1), out _, out var blob);
        Assert.Equal(Blob(1), blob);
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task Reopen_SameFingerprint_ServesBlocksAndVersionsFromDisk()
    {
        using var dir = new TempDirectory();
        var first = Open(dir);
        first.Put(Key(1), 4, 11, Blob(1));
        first.Put(Key(2), 0, 22, Blob(2));
        await CloseCleanly(first);

        var second = Open(dir);

        Assert.Equal(TerrainBlockLookup.Found, second.TryLoad(Key(1), out var version, out var blob));
        Assert.Equal(4u, version);
        Assert.Equal(Blob(1), blob);
        Assert.Equal(TerrainBlockLookup.Found, second.TryLoad(Key(2), out version, out _));
        Assert.Equal(0u, version);
        Assert.True(second.TryGetHash(Key(2), out var hash));
        Assert.Equal(22ul, hash);
        Assert.Equal(new Dictionary<TerrainBlockKey, uint> { [Key(1)] = 4, [Key(2)] = 0 }, Versions(second, WorldFactory.HomePlanet));
        await CloseCleanly(second);
    }

    [Fact]
    public async Task Reopen_DifferentFingerprint_DropsClientRows()
    {
        using var dir = new TempDirectory();
        var first = Open(dir);
        first.Put(Key(1), 4, 11, Blob(1));
        await CloseCleanly(first);

        var logger = new ListLogger();
        var second = Open(dir, fingerprint: "build-2/profile-1", logger: logger);

        Assert.Equal(TerrainBlockLookup.NotEdited, second.TryLoad(Key(1), out _, out _));
        Assert.Empty(Versions(second, WorldFactory.HomePlanet));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
        await CloseCleanly(second);

        var third = Open(dir, fingerprint: "build-2/profile-1");
        Assert.Equal(TerrainBlockLookup.NotEdited, third.TryLoad(Key(1), out _, out _));
        await CloseCleanly(third);
    }

    [Fact]
    public async Task Expect_MissingKnownBlock_IsUnavailableUntilMatchingPut()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);

        cache.Expect(Key(1), 5, 77);
        Assert.Equal(TerrainBlockLookup.Unavailable, cache.TryLoad(Key(1), out var version, out var blob));
        Assert.Equal(0u, version);
        Assert.Empty(blob);
        Assert.Equal(TerrainBlockLookup.NotEdited, cache.TryLoad(Key(2), out _, out _));

        cache.Put(Key(1), 5, 77, Blob(1));
        Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(1), out version, out _));
        Assert.Equal(5u, version);
        await CloseCleanly(cache);
    }

    [Theory]
    [InlineData(9u, 77ul)]
    [InlineData(5u, 78ul)]
    public async Task Expect_CachedVersionAheadOrHashDiffers_IsUnavailableUntilAuthoritativePut(uint cachedVersion, ulong cachedHash)
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.Put(Key(1), cachedVersion, cachedHash, Blob(1));

        cache.Expect(Key(1), 5, 77);
        Assert.Equal(TerrainBlockLookup.Unavailable, cache.TryLoad(Key(1), out _, out _));

        cache.Put(Key(1), 5, 77, Blob(2));
        Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(1), out var version, out var blob));
        Assert.Equal(5u, version);
        Assert.Equal(Blob(2), blob);
        Assert.True(cache.TryGetHash(Key(1), out var hash));
        Assert.Equal(77ul, hash);
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task Expect_MatchingCachedBlock_StaysFound()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.Put(Key(1), 5, 77, Blob(1));

        cache.Expect(Key(1), 5, 77);

        Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(1), out _, out _));
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task Expect_VersionZeroOverEditedCache_DropsBlockToGenerator()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.Put(Key(1), 5, 77, Blob(1));

        cache.Expect(Key(1), 0, 1234);

        Assert.Equal(TerrainBlockLookup.NotEdited, cache.TryLoad(Key(1), out _, out _));
        Assert.False(cache.TryGetHash(Key(1), out _));
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task Invalidate_RemovesBlockFromMemoryAndDisk()
    {
        using var dir = new TempDirectory();
        var first = Open(dir);
        first.Put(Key(1), 1, 1, Blob(1));
        first.Put(Key(2), 1, 2, Blob(2));
        first.Invalidate(Key(1));
        Assert.Equal(TerrainBlockLookup.NotEdited, first.TryLoad(Key(1), out _, out _));
        await CloseCleanly(first);

        var second = Open(dir);
        Assert.Equal(TerrainBlockLookup.NotEdited, second.TryLoad(Key(1), out _, out _));
        Assert.Equal(TerrainBlockLookup.Found, second.TryLoad(Key(2), out _, out _));
        await CloseCleanly(second);
    }

    [Fact]
    public async Task CopyVersions_FiltersPlanetAndPagesInKeyOrder()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        foreach (var i in new[] { 5, 1, 4, 2, 3 })
        {
            cache.Put(Key(i), (uint)i, (ulong)i, Blob(i));
        }

        cache.Put(Key(1, _otherPlanet), 9, 9, Blob(9));

        var page = new TerrainBlockVersion[2];
        Assert.Equal(2, cache.CopyVersions(WorldFactory.HomePlanet, page, 0));
        Assert.Equal([Key(1), Key(2)], page.Select(v => v.Key));
        Assert.Equal(2, cache.CopyVersions(WorldFactory.HomePlanet, page, 2));
        Assert.Equal([Key(3), Key(4)], page.Select(v => v.Key));
        Assert.Equal(1, cache.CopyVersions(WorldFactory.HomePlanet, page, 4));
        Assert.Equal(new TerrainBlockVersion(Key(5), 5), page[0]);
        Assert.Equal(0, cache.CopyVersions(WorldFactory.HomePlanet, page, 5));
        Assert.Equal(1, cache.CopyVersions(_otherPlanet, page, 0));
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task MemoryLimit_EvictsLeastRecentlyUsedButKeepsPinnedAndStillServesFromDisk()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.MaxMemoryBytes = 100;
        cache.Pin(Key(1));
        for (var i = 1; i <= 4; i++)
        {
            cache.Put(Key(i), 1, (ulong)i, Blob(i, 40));
        }

        await cache.FlushAsync().WaitAsync(_wait, TestContext.Current.CancellationToken);

        Assert.True(cache.MemoryBytes <= 100, $"memory {cache.MemoryBytes}");
        Assert.True(cache.MemoryBytes >= 40, "the pinned block stays in memory");
        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(i), out _, out var blob));
            Assert.Equal(Blob(i, 40), blob);
        }

        cache.Unpin(Key(1));
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task DiskLimit_EvictsOldestUnpinnedBlocks()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.MaxDiskBytes = 100;
        cache.Pin(Key(1));
        for (var i = 1; i <= 4; i++)
        {
            cache.Put(Key(i), 1, (ulong)i, Blob(i, 40));
            await cache.FlushAsync().WaitAsync(_wait, TestContext.Current.CancellationToken);
        }

        await cache.FlushAsync().WaitAsync(_wait, TestContext.Current.CancellationToken);
        Assert.True(cache.DiskBytes <= 100, $"disk {cache.DiskBytes}");
        var versions = Versions(cache, WorldFactory.HomePlanet);
        Assert.Contains(Key(1), versions);
        Assert.Contains(Key(4), versions);
        Assert.DoesNotContain(Key(2), versions);
        await CloseCleanly(cache);

        var reopened = Open(dir);
        Assert.Equal(versions, Versions(reopened, WorldFactory.HomePlanet));
        await CloseCleanly(reopened);
    }

    [Fact]
    public async Task DiskLimit_EvictedEditedBlock_IsUnavailableNotPristine()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.MaxDiskBytes = 100;
        for (var i = 1; i <= 4; i++)
        {
            cache.Put(Key(i), 1, (ulong)i, Blob(i, 40));
            await cache.FlushAsync().WaitAsync(_wait, TestContext.Current.CancellationToken);
        }

        await cache.FlushAsync().WaitAsync(_wait, TestContext.Current.CancellationToken);
        Assert.False(cache.TryGetHeld(Key(1), out _, out _));
        Assert.Equal(TerrainBlockLookup.Unavailable, cache.TryLoad(Key(1), out _, out _));

        cache.Put(Key(1), 1, 1, Blob(1, 40));
        Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(1), out _, out _));
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task MemoryLimit_EvictedUnsavedEditedBlock_IsUnavailableNotPristine()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.MaxMemoryBytes = 100;
        using var release = new ManualResetEventSlim();
        cache.BeforeBatch = () => release.Wait(_wait, TestContext.Current.CancellationToken);

        // 300 puts overflow the 256-entry write queue, so the overflow blocks stay memory-only and are forgotten by eviction.
        for (var i = 0; i < 300; i++)
        {
            cache.Put(Key(i), 1, (ulong)i, Blob(i, 8));
        }

        var forgotten = Enumerable.Range(0, 300).Where(i => !cache.TryGetHeld(Key(i), out _, out _)).ToList();
        Assert.NotEmpty(forgotten);
        foreach (var i in forgotten)
        {
            Assert.Equal(TerrainBlockLookup.Unavailable, cache.TryLoad(Key(i), out _, out _));
        }

        release.Set();
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task WriteQueue_IsBoundedTo256ButMemoryServesEverything()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        using var release = new ManualResetEventSlim();
        cache.BeforeBatch = () => release.Wait(_wait, TestContext.Current.CancellationToken);

        for (var i = 0; i < 300; i++)
        {
            cache.Put(Key(i), 1, (ulong)i, Blob(i, 8));
        }

        for (var i = 0; i < 300; i++)
        {
            Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(i), out _, out _));
        }

        release.Set();
        await CloseCleanly(cache);

        var reopened = Open(dir);
        Assert.Equal(256, reopened.Count);
        await CloseCleanly(reopened);
    }

    [Fact]
    public async Task Put_SameKeyRepeatedly_CoalescesToTheLatestVersion()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        using var release = new ManualResetEventSlim();
        cache.BeforeBatch = () => release.Wait(_wait, TestContext.Current.CancellationToken);
        for (uint v = 1; v <= 600; v++)
        {
            cache.Put(Key(1), v, v, Blob((int)v, 8));
        }

        release.Set();
        await CloseCleanly(cache);

        var reopened = Open(dir);
        Assert.Equal(TerrainBlockLookup.Found, reopened.TryLoad(Key(1), out var version, out var blob));
        Assert.Equal(600u, version);
        Assert.Equal(Blob(600, 8), blob);
        await CloseCleanly(reopened);
    }

    [Fact]
    public async Task WriterDiskError_LogsFallsBackToMemoryOnlyAndKeepsServing()
    {
        using var dir = new TempDirectory();
        var logger = new ListLogger();
        var cache = Open(dir, logger: logger);
        cache.BeforeBatch = () => throw new SqliteException("database or disk is full", SqliteFull);

        cache.Put(Key(1), 1, 1, Blob(1));
        await cache.FlushAsync().WaitAsync(_wait, TestContext.Current.CancellationToken);

        Assert.True(cache.IsMemoryOnly);
        Assert.Contains(logger.Entries, e => e.Level >= LogLevel.Warning && e.Exception is SqliteException);
        cache.Put(Key(2), 1, 2, Blob(2));
        Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(1), out _, out var blob));
        Assert.Equal(Blob(1), blob);
        Assert.Equal(TerrainBlockLookup.Found, cache.TryLoad(Key(2), out _, out _));
        await CloseCleanly(cache);
    }

    [Fact]
    public async Task Open_CorruptDatabaseFile_RecreatesItAndLogs()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(dir.File("cache/terrain.db"))!);
        await File.WriteAllBytesAsync(dir.File("cache/terrain.db"), Enumerable.Range(0, 8192).Select(i => (byte)(i * 7 + 3)).ToArray(), TestContext.Current.CancellationToken);
        var logger = new ListLogger();

        var cache = Open(dir, logger: logger);

        Assert.False(cache.IsMemoryOnly);
        Assert.Contains(logger.Entries, e => e.Level >= LogLevel.Warning);
        cache.Put(Key(1), 2, 2, Blob(1));
        await CloseCleanly(cache);
        var reopened = Open(dir);
        Assert.Equal(TerrainBlockLookup.Found, reopened.TryLoad(Key(1), out _, out _));
        await CloseCleanly(reopened);
    }

    [Fact]
    public async Task Close_ActiveDiskRead_WaitsFiveSecondsThenReportsFalseWithoutBreakingTheRead()
    {
        using var dir = new TempDirectory();
        var seed = Open(dir);
        seed.Put(Key(1), 3, 3, Blob(1));
        await CloseCleanly(seed);

        var time = new FakeTimeProvider();
        var cache = Open(dir, time);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        cache.BeforeRead = _ =>
        {
            entered.Set();
            release.Wait(_wait, TestContext.Current.CancellationToken);
        };
        var read = Task.Run(() => (Lookup: cache.TryLoad(Key(1), out var version, out var blob), Version: version, Blob: blob));
        Assert.True(entered.Wait(_wait, TestContext.Current.CancellationToken));

        var close = cache.CloseAsync();
        Assert.False(close.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(close.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(await close.WaitAsync(_wait, TestContext.Current.CancellationToken));

        release.Set();
        var result = await read.WaitAsync(_wait, TestContext.Current.CancellationToken);
        Assert.Equal(TerrainBlockLookup.Found, result.Lookup);
        Assert.Equal(3u, result.Version);
        Assert.Equal(Blob(1), result.Blob);
        Assert.True(await cache.CloseAsync().WaitAsync(_wait, TestContext.Current.CancellationToken));
        Assert.Equal(TerrainBlockLookup.Unavailable, cache.TryLoad(Key(1), out _, out _));
    }

    [Fact]
    public async Task Close_WithoutActiveReads_FlushesWriterAndRefusesFurtherReads()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        cache.Put(Key(1), 1, 1, Blob(1));

        Assert.True(await cache.CloseAsync().WaitAsync(_wait, TestContext.Current.CancellationToken));

        Assert.Equal(TerrainBlockLookup.Unavailable, cache.TryLoad(Key(1), out _, out _));
        cache.Put(Key(2), 1, 2, Blob(2));
        var reopened = Open(dir);
        Assert.Equal(TerrainBlockLookup.Found, reopened.TryLoad(Key(1), out _, out _));
        Assert.Equal(TerrainBlockLookup.NotEdited, reopened.TryLoad(Key(2), out _, out _));
        await CloseCleanly(reopened);
    }

    [Fact]
    public async Task ParallelPutsAndLoads_DoNotCorruptTheIndex()
    {
        using var dir = new TempDirectory();
        var cache = Open(dir);
        var writers = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                cache.Put(Key(t * 1000 + i), 1, (ulong)i, Blob(i, 16));
                cache.TryLoad(Key(t * 1000 + i / 2), out _, out _);
            }
        }));
        await Task.WhenAll(writers).WaitAsync(_wait, TestContext.Current.CancellationToken);

        Assert.Equal(800, cache.Count);
        await CloseCleanly(cache);
        var reopened = Open(dir);
        Assert.InRange(reopened.Count, 1, 800);
        await CloseCleanly(reopened);
    }

    private sealed class ListLogger : ILogger<ClientTerrainCache>
    {
        private readonly Lock _lock = new();
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock)
            {
                _entries.Add(new LogEntry(logLevel, eventId, exception, formatter(state, exception)));
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, Exception? Exception, string Message);
}
