using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests.Terrain;

[Collection(SqlitePools.Name)]
public sealed class ServerTerrainBlocksLimitTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(30);

    private static TerrainBlockKey Key(int i) => new(WorldFactory.HomePlanet, 0, i, 250, 0);

    private static async Task<string> SavedWorld(TempDirectory dir, TerrainBlockKey key)
    {
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());
        await using var writer = new PersistenceWriter(
            WorldDatabase.Open(dir.WorldPath), WorldHarness.Options, new FakeTimeProvider(), Faults.NoFaults.Instance,
            NullLogger<PersistenceWriter>.Instance);
        Assert.True(await writer.CheckpointAsync(new CheckpointRequest(
            ServerTerrainBlocksTests.Checkpoint(new TerrainBlockVersion(key, 1)), [new TerrainBlockPayload(key, 1, ServerTerrainBlocksTests.Blob(key, 1))])));
        return dir.WorldPath;
    }

    [Fact]
    public async Task Thresholds_EarlyCheckpointAndSaturation_ClearAfterCommit()
    {
        using var dir = new TempDirectory();
        using var store = new TerrainBlockStore(await SavedWorld(dir, Key(100)));
        var blocks = ServerTerrainBlocksTests.NewBlocks(store, options: new TerrainBufferOptions { EarlyCheckpointBlocks = 3, MaxPendingBlocks = 5 });

        for (var i = 0; i < 2; i++)
        {
            blocks.Record(Key(i), 1, ServerTerrainBlocksTests.Blob(Key(i), 1));
        }

        Assert.False(blocks.NeedsEarlyCheckpoint);
        blocks.Record(Key(2), 1, ServerTerrainBlocksTests.Blob(Key(2), 1));
        Assert.True(blocks.NeedsEarlyCheckpoint);
        Assert.False(blocks.IsSaturated);
        blocks.Record(Key(3), 1, ServerTerrainBlocksTests.Blob(Key(3), 1));
        blocks.Record(Key(4), 1, ServerTerrainBlocksTests.Blob(Key(4), 1));
        Assert.True(blocks.IsSaturated);
        blocks.Record(Key(0), 2, ServerTerrainBlocksTests.Blob(Key(0), 2));
        Assert.Equal(5, blocks.PendingCount);

        var request = blocks.CreateCheckpointRequest(ServerTerrainBlocksTests.Checkpoint(
            new TerrainBlockVersion(Key(1), 1), new TerrainBlockVersion(Key(2), 1), new TerrainBlockVersion(Key(3), 1)));
        blocks.OnCheckpointFailed(request);
        Assert.True(blocks.IsSaturated);
        blocks.OnCheckpointCommitted(request);

        Assert.Equal(2, blocks.PendingCount);
        Assert.False(blocks.IsSaturated);
        Assert.False(blocks.NeedsEarlyCheckpoint);
    }

    [Fact]
    public void Options_Invalid_Throw()
    {
        using var dir = new TempDirectory();
        using var store = new TerrainBlockStore(dir.WorldPath);

        Assert.ThrowsAny<ArgumentException>(() => ServerTerrainBlocksTests.NewBlocks(store, options: new TerrainBufferOptions { EarlyCheckpointBlocks = 0 }));
        Assert.ThrowsAny<ArgumentException>(() => ServerTerrainBlocksTests.NewBlocks(
            store, options: new TerrainBufferOptions { EarlyCheckpointBlocks = 10, MaxPendingBlocks = 9 }));
        Assert.Throws<ArgumentException>(() => ServerTerrainBlocksTests.NewBlocks(store, [new TerrainBlockVersion(Key(0), 0)]));
        Assert.Throws<ArgumentException>(() => ServerTerrainBlocksTests.NewBlocks(store, [new TerrainBlockVersion(Key(0), 1), new TerrainBlockVersion(Key(0), 2)]));
    }

    [Fact]
    public async Task CloseAsync_ReadInProgress_WaitsForIt_LaterReadsUnavailableWithoutSqlite()
    {
        using var dir = new TempDirectory();
        var key = Key(7);
        using var store = new TerrainBlockStore(await SavedWorld(dir, key));
        var time = new FakeTimeProvider();
        var blocks = ServerTerrainBlocksTests.NewBlocks(store, [new TerrainBlockVersion(key, 1)], time: time);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        store.BeforeQuery = _ =>
        {
            entered.Set();
            Assert.True(release.Wait(_wait));
        };
        var read = Task.Factory.StartNew(() => blocks.TryLoad(key, out _, out _), TaskCreationOptions.LongRunning);
        Assert.True(entered.Wait(_wait, TestContext.Current.CancellationToken));

        var close = blocks.CloseAsync();

        Assert.False(close.IsCompleted);
        var queries = store.QueryCount;
        Assert.Equal(TerrainBlockLookup.Unavailable, blocks.TryLoad(key, out _, out _));
        Assert.Equal(TerrainBlockLookup.Unavailable, blocks.TryLoad(Key(99), out _, out _));
        Assert.Equal(queries, store.QueryCount);
        release.Set();
        Assert.True(await close.WaitAsync(_wait, TestContext.Current.CancellationToken));
        Assert.Equal(TerrainBlockLookup.Found, await read.WaitAsync(_wait, TestContext.Current.CancellationToken));
        Assert.True(await blocks.CloseAsync());
    }

    [Fact]
    public async Task CloseAsync_ReadOutlivesTheDeadline_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var key = Key(7);
        using var store = new TerrainBlockStore(await SavedWorld(dir, key));
        var time = new FakeTimeProvider();
        var blocks = ServerTerrainBlocksTests.NewBlocks(
            store, [new TerrainBlockVersion(key, 1)], new TerrainBufferOptions { CloseTimeout = TimeSpan.FromSeconds(5) }, time);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        store.BeforeQuery = _ =>
        {
            entered.Set();
            Assert.True(release.Wait(_wait));
        };
        var read = Task.Factory.StartNew(() => blocks.TryLoad(key, out _, out _), TaskCreationOptions.LongRunning);
        Assert.True(entered.Wait(_wait, TestContext.Current.CancellationToken));

        var close = blocks.CloseAsync();
        time.Advance(TimeSpan.FromSeconds(4.9));
        Assert.False(close.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(0.1));

        Assert.False(await close.WaitAsync(_wait, TestContext.Current.CancellationToken));
        release.Set();
        await read.WaitAsync(_wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CloseAsync_NoReads_CompletesAtOnce()
    {
        using var dir = new TempDirectory();
        using var store = new TerrainBlockStore(await SavedWorld(dir, Key(1)));
        var blocks = ServerTerrainBlocksTests.NewBlocks(store, [new TerrainBlockVersion(Key(1), 1)]);
        blocks.Record(Key(2), 1, ServerTerrainBlocksTests.Blob(Key(2), 1));

        Assert.True(await blocks.CloseAsync());

        Assert.Equal(TerrainBlockLookup.Unavailable, blocks.TryLoad(Key(2), out _, out _));
        Assert.Equal(0, store.QueryCount);
    }
}
