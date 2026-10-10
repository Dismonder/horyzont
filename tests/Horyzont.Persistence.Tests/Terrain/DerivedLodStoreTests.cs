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
}
