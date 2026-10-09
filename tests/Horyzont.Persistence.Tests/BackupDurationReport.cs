using System.Diagnostics;
using System.Globalization;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Backup;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>
/// E2 of the K1 gate review: how long a backup of a world with a realistic terrain takes (50 000 blocks of about 3 KB), with the
/// copy methods that are candidates for running it off the writer thread. The numbers go to the PR and the K2-09 plan.
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class BackupDurationReport
{
    private const int Blocks = 50_000;
    private const int BlockBytes = 3 * 1024;
    private const int PerCheckpoint = 5_000;

    [Fact]
    [Trait("Category", "Manual")]
    public async Task Backup_FiftyThousandBlocks_Duration()
    {
        using var dir = new TempDirectory();
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());
        await using (var writer = new PersistenceWriter(
            WorldDatabase.Open(dir.WorldPath), WorldHarness.Options, new FakeTimeProvider(), Faults.NoFaults.Instance,
            NullLogger<PersistenceWriter>.Instance))
        {
            var random = new Random(1);
            for (var start = 0; start < Blocks; start += PerCheckpoint)
            {
                var versions = new List<TerrainBlockVersion>(PerCheckpoint);
                var payloads = new List<TerrainBlockPayload>(PerCheckpoint);
                for (var i = start; i < start + PerCheckpoint; i++)
                {
                    var key = new TerrainBlockKey(WorldFactory.HomePlanet, 0, i % 100, 250 + (i / 10_000), i / 100 % 100);
                    var data = new byte[BlockBytes - 256 + random.Next(512)];
                    random.NextBytes(data);
                    versions.Add(new TerrainBlockVersion(key, 1));
                    payloads.Add(new TerrainBlockPayload(key, 1, data));
                }

                versions.Sort((a, b) => a.Key.CompareTo(b.Key));
                var core = new WorldCheckpoint(OpId.None, new Tick(1), 1, 1, [], [], [], [], [], versions);
                Assert.True(await writer.CheckpointAsync(new CheckpointRequest(core, payloads)));
            }
        }

        var size = new FileInfo(dir.WorldPath).Length;
        var lines = new List<string> { string.Create(CultureInfo.InvariantCulture, $"world: {Blocks} blocks, {size / (1024.0 * 1024.0):F1} MiB") };
        for (var round = 0; round < 3; round++)
        {
            lines.Add(Measure("BackupService on the writer's read-write connection", () =>
            {
                using var db = WorldDatabase.Open(dir.WorldPath);
                new BackupService(dir.File("a"), 1, TimeProvider.System).CreateBackup(db.Connection, "measure");
            }));
            lines.Add(Measure("BackupDatabase from a separate read-only connection", () =>
            {
                using var source = ReadOnly(dir.WorldPath);
                new BackupService(dir.File("b"), 1, TimeProvider.System).CreateBackup(source, "measure");
            }));
            lines.Add(Measure("VACUUM INTO from a separate read-only connection", () =>
            {
                var target = dir.File($"vacuum-{round}.db");
                using var source = ReadOnly(dir.WorldPath);
                using var command = source.CreateCommand();
                command.CommandText = "VACUUM INTO $target;";
                command.Parameters.AddWithValue("$target", target);
                command.ExecuteNonQuery();
            }));
        }

        foreach (var line in lines)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(line);
            Console.WriteLine(line);
        }
    }

    private static SqliteConnection ReadOnly(string path) =>
        WorldDatabase.OpenConnection(WorldDatabase.ConnectionString(Path.GetFullPath(path), SqliteOpenMode.ReadOnly, pooling: false), readOnly: true);

    private static string Measure(string name, Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return string.Create(CultureInfo.InvariantCulture, $"{name}: {watch.Elapsed.TotalMilliseconds:F0} ms");
    }
}
