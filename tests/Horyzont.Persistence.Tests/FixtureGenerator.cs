using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>
/// Writes <c>Fixtures/world-v1.db</c> into the source tree. Run once by hand when schema version 1 is final and commit the file;
/// never regenerate it afterwards, because it stands for worlds saved by released versions.
/// </summary>
public sealed class FixtureGenerator
{
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Generate_WorldV1()
    {
        var target = Path.Combine(RepoRoot(), "tests", "Horyzont.Persistence.Tests", "Fixtures", MigrationTests.FixtureName);
        Assert.False(File.Exists(target), $"{target} exists; delete it first if it really must be regenerated.");
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, time: time))
        {
            var script = new WorldScript(2026);
            for (var i = 0; i < 120; i++)
            {
                script.Step(h.Sim);
            }

            Assert.True(await h.CheckpointAndPump());
            for (var i = 0; i < 30; i++)
            {
                script.Step(h.Sim);
            }

            await h.FlushAndPump();
        }

        // Fold the WAL into the main file (the repository ignores *-wal files); pooled terrain readers must close first.
        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "PRAGMA journal_mode = DELETE;");
        Assert.False(File.Exists(dir.WorldPath + "-wal"));
        File.Copy(dir.WorldPath, target);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Horyzont.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
