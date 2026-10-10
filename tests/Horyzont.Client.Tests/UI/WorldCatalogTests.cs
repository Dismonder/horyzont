using Horyzont.Client.UI;

namespace Horyzont.Client.Tests.UI;

public sealed class WorldCatalogTests
{
    [Fact]
    public async Task List_MixedGuidDirectories_SortsWithoutOpeningDatabase()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var oldId = Guid.NewGuid();
            var newId = Guid.NewGuid();
            var absentId = Guid.NewGuid();
            foreach (var id in new[] { oldId, newId, absentId })
                Directory.CreateDirectory(Path.Combine(root, id.ToString("D")));
            Directory.CreateDirectory(Path.Combine(root, "not-a-world"));
            Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N")));
            string oldDb = Path.Combine(root, oldId.ToString("D"), "world.db");
            string newDb = Path.Combine(root, newId.ToString("D"), "world.db");
            byte[] corrupt = [0, 255, 42];
            await File.WriteAllBytesAsync(oldDb, corrupt);
            await File.WriteAllBytesAsync(newDb, corrupt);
            File.SetLastWriteTimeUtc(oldDb, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newDb, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var catalog = new WorldCatalog();
            var entries = await catalog.ListAsync(root, TestContext.Current.CancellationToken);
            Assert.Equal(3, entries.Count);
            Assert.True(entries.Single(e => e.WorldId == newId).DatabasePresent);
            Assert.False(entries.Single(e => e.WorldId == absentId).DatabasePresent);
            Assert.True(entries.ToList().FindIndex(e => e.WorldId == newId) < entries.ToList().FindIndex(e => e.WorldId == oldId));
            Assert.Equal(corrupt, await File.ReadAllBytesAsync(oldDb));
            Assert.Equal(corrupt, await File.ReadAllBytesAsync(newDb));
            Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task List_OverLimit_SignalsTruncationAndHonorsCancellation()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 1025; i++)
                Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("D")));
            var catalog = new WorldCatalog();
            Assert.Equal(1024, (await catalog.ListAsync(root, TestContext.Current.CancellationToken)).Count);
            Assert.True(catalog.LimitExceeded);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ListAsync(root, cancelled.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task List_MissingDirectory_ReturnsEmpty()
    {
        var catalog = new WorldCatalog();
        Assert.Empty(await catalog.ListAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D")), TestContext.Current.CancellationToken));
    }
}
