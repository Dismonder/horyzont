using System.Collections.Concurrent;
using System.Numerics;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests.Terrain;

[Collection(SqlitePools.Name)]
public sealed class ServerTerrainBlocksConcurrencyTests
{
    private static readonly Vector3[] _centers =
        [new(0.5f, 3999.5f, 0.5f), new(15.5f, 3999f, -0.5f), new(-16f, 3990f, 16f), new(1f, 4001f, 30f)];

    /// <summary>
    /// Eight threads read blocks while the server thread edits, checkpoints and commits: every (version, blob) read is at least the
    /// version published (Record returned) before the call, equals the blob recorded for that version, and per thread the version of a key never decreases.
    /// </summary>
    [Fact]
    public async Task TryLoad_EightThreadsDuringEditsAndCheckpoints_PairsMatchOracleAndNeverGoBack()
    {
        using var dir = new TempDirectory();
        await using var server = TerrainServer.CreateNew(dir.WorldPath);
        var keys = new SortedSet<TerrainBlockKey>();
        var scratch = new List<TerrainBlockKey>();
        foreach (var center in _centers)
        {
            TerrainMath.AffectedBlocks(WorldFactory.HomePlanet, center, 1f, 1f, TerrainRules.EditMarginVoxels, scratch);
            keys.UnionWith(scratch);
        }

        keys.Add(new TerrainBlockKey(WorldFactory.HomePlanet, 0, 1000, 0, 0));
        var keyArray = keys.ToArray();
        var failures = new ConcurrentQueue<string>();
        var reads = new long[8];
        using var stop = new CancellationTokenSource();

        var readers = Enumerable.Range(0, 8).Select(t => Task.Factory.StartNew(
            () =>
            {
                var random = new Random(t);
                var seen = new Dictionary<TerrainBlockKey, uint>();
                while (!stop.IsCancellationRequested)
                {
                    var key = keyArray[random.Next(keyArray.Length)];
                    var published = server.Published.GetValueOrDefault(key);
                    var result = server.Blocks.TryLoad(key, out var version, out var blob);
                    reads[t]++;
                    if (result == TerrainBlockLookup.Unavailable)
                    {
                        failures.Enqueue($"{key} unavailable");
                        continue;
                    }

                    if (result == TerrainBlockLookup.NotEdited)
                    {
                        if (published != 0)
                        {
                            failures.Enqueue($"{key} not edited although version {published} was published before the call");
                        }
                    }
                    else if (version < published)
                    {
                        failures.Enqueue($"{key} returned version {version} below the published {published}");
                    }
                    else if (!server.Recorded.TryGetValue((key, version), out var expected) || !expected.AsSpan().SequenceEqual(blob))
                    {
                        failures.Enqueue($"{key} version {version} has data of another version");
                    }

                    var previous = seen.GetValueOrDefault(key);
                    if (version < previous)
                    {
                        failures.Enqueue($"{key} went back from {previous} to {version}");
                    }

                    seen[key] = Math.Max(previous, version);
                }
            },
            TaskCreationOptions.LongRunning)).ToArray();

        var random = new Random(1);
        for (var round = 0; round < 120; round++)
        {
            server.Sim.ApplyServerOperation(ServerTerrainBlocksTests.Dig(_centers[random.Next(_centers.Length)]));
            if (round % 7 == 3 && !server.State.IsCheckpointInFlight)
            {
                Assert.True(await server.RequestCheckpoint());
            }

            if (round % 5 == 0)
            {
                await Task.Yield();
            }

            server.Pump();
        }

        await stop.CancelAsync();
        await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Empty(failures);
        Assert.All(reads, count => Assert.True(count > 0));
        foreach (var version in server.State.TerrainVersions.All)
        {
            Assert.Equal(TerrainBlockLookup.Found, server.Blocks.TryLoad(version.Key, out var current, out _));
            Assert.Equal(version.Version, current);
        }
    }
}
