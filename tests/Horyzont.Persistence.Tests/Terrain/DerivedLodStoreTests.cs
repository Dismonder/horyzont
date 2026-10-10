using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests.Terrain;

[Collection("SqlitePools")]
public sealed class DerivedLodStoreTests
{
    [Fact]
    public async Task Rebuild_DerivedLodAfterRestart_LoadsOnlyMatchingFingerprint()
    {
        var path = Path.Combine(Path.GetTempPath(), "horyzont-derived-" + Guid.NewGuid().ToString("N") + ".db");
        var time = new FakeTimeProvider();
        var key = new TerrainBlockKey(new PlanetId(1), 2, -2, 3, -4);
        var first = new DerivedLodStore(path, time, NullLogger<DerivedLodStore>.Instance);
        var blob = new byte[] { 1, 2, 3 };
        Assert.True(first.Put(key, "profile:1;children:7", blob));
        blob[0] = 9;
        Assert.True(first.TryLoad(key, "profile:1;children:7", out var current));
        Assert.Equal(1, current[0]);
        Assert.True(await first.CloseAsync());
        var second = new DerivedLodStore(path, time, NullLogger<DerivedLodStore>.Instance);
        Assert.True(second.TryLoad(key, "profile:1;children:7", out var restored));
        Assert.Equal(new byte[] { 1, 2, 3 }, restored);
        Assert.False(second.TryLoad(key, "profile:1;children:8", out _));
        Assert.True(await second.CloseAsync());
        File.Delete(path);
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), "horyzont-derived-" + Guid.NewGuid().ToString("N") + ".db");

    private static TerrainBlockKey Key(int x) => new(new PlanetId(1), 1, x, 0, 0);

    [Fact]
    public async Task Put_MoreThanTheLimit_KeepsTheMostRecentlyUsedInMemory()
    {
        var path = TempPath();
        var store = new DerivedLodStore(path, new FakeTimeProvider(), NullLogger<DerivedLodStore>.Instance, maxEntries: 16);
        for (var i = 0; i < 16; i++)
        {
            Assert.True(store.Put(Key(i), "f", [(byte)i]));
        }

        // Key 0 is the oldest write but the most recently read; key 1 is the least recently used.
        Assert.True(store.TryLoad(Key(0), "f", out _));
        Assert.True(store.Put(Key(100), "f", [1]));

        Assert.True(store.TryLoad(Key(0), "f", out _));
        Assert.False(store.TryLoad(Key(1), "f", out _));
        Assert.True(store.TryLoad(Key(100), "f", out _));
        Assert.True(await store.CloseAsync());
        File.Delete(path);
    }

    [Fact]
    public async Task Put_ManyMoreThanTheLimit_TrimsTheFileAndKeepsTheNewest()
    {
        var path = TempPath();
        var time = new FakeTimeProvider();
        var store = new DerivedLodStore(path, time, NullLogger<DerivedLodStore>.Instance, maxEntries: 16);
        for (var i = 0; i < 200; i++)
        {
            while (!store.Put(Key(i), "f", [(byte)i]))
            {
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }

        Assert.True(await store.CloseAsync());
        var reopened = new DerivedLodStore(path, time, NullLogger<DerivedLodStore>.Instance, maxEntries: 1000);
        var kept = Enumerable.Range(0, 200).Count(i => reopened.TryLoad(Key(i), "f", out _));
        Assert.InRange(kept, 16, 16);
        Assert.True(reopened.TryLoad(Key(199), "f", out _));
        Assert.True(await reopened.CloseAsync());
        File.Delete(path);
    }

    [Fact]
    public async Task CloseAsync_WriterThrowsAnythingButSqlite_ReturnsFalseWithoutThrowing()
    {
        var path = TempPath();
        var store = new DerivedLodStore(path, new FakeTimeProvider(), NullLogger<DerivedLodStore>.Instance)
        {
            BeforeWrite = static () => throw new InvalidOperationException("injected"),
        };
        Assert.True(store.Put(Key(1), "f", [1]));

        Assert.False(await store.CloseAsync());
        Assert.True(store.IsWriterFailed);
        File.Delete(path);
    }

    // The call sites of the game (DerivedTerrainTicker, the server factory cleanup and the client provider) hand the close of the
    // derived store and the release of its owner to TerrainFailureCleanup; this is the same pairing with the real store.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAfterCloseAsync_WithTheRealStore_DisposesOnlyAfterACleanClose(bool writerFails)
    {
        var path = TempPath();
        var store = new DerivedLodStore(path, new FakeTimeProvider(), NullLogger<DerivedLodStore>.Instance);
        if (writerFails)
        {
            store.BeforeWrite = static () => throw new InvalidOperationException("injected");
        }

        Assert.True(store.Put(Key(1), "f", [1]));
        var disposed = false;

        var result = await Horyzont.Game.Server.TerrainFailureCleanup.DisposeAfterCloseAsync(store.CloseAsync, () => disposed = true);

        Assert.Equal(!writerFails, result);
        Assert.Equal(!writerFails, disposed);
        File.Delete(path);
    }
}
