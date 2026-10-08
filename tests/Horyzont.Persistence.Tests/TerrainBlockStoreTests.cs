using System.Buffers.Binary;
using System.Collections.Concurrent;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class TerrainBlockStoreTests
{
    private static readonly TerrainBlockKey[] _keys =
        [.. Enumerable.Range(0, 16).Select(i => new TerrainBlockKey(WorldFactory.HomePlanet, (byte)(i % 3), i, -i, i * 7))];

    /// <summary>Data that encodes its key and version, so a torn pair is detectable.</summary>
    private static byte[] Data(TerrainBlockKey key, uint version)
    {
        var data = new byte[512 + (int)(version % 64)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, version);
        for (var i = 4; i < data.Length; i++)
        {
            data[i] = (byte)((key.X * 31) + (int)version + i);
        }

        return data;
    }

    private static CheckpointRequest Checkpoint(uint version)
    {
        var versions = _keys.Select(k => new TerrainBlockVersion(k, version)).ToList();
        var core = new WorldCheckpoint(OpId.None, new Tick(version), 1, 1, [], [], [], [], [], versions);
        return new CheckpointRequest(core, [.. _keys.Select(k => new TerrainBlockPayload(k, version, Data(k, version)))]);
    }

    [Fact]
    public void TryLoad_UnsavedBlock_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var world = WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [], [], []);
        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, new FakeTimeProvider());

        Assert.False(new TerrainBlockStore(dir.WorldPath).TryLoad(_keys[0], out var version, out var data));
        Assert.Equal(0u, version);
        Assert.Empty(data);
    }

    [Fact]
    public async Task TryLoad_EightThreadsDuringCheckpoints_ConsistentVersionDataPairs()
    {
        using var dir = new TempDirectory();
        var world = WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [], [], []);
        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, new FakeTimeProvider());
        await using var writer = new PersistenceWriter(
            WorldDatabase.Open(dir.WorldPath), WorldHarness.Options, new FakeTimeProvider(), Faults.NoFaults.Instance,
            NullLogger<PersistenceWriter>.Instance);
        Assert.True(await writer.CheckpointAsync(Checkpoint(1)));
        var store = new TerrainBlockStore(dir.WorldPath);
        var failures = new ConcurrentQueue<string>();
        var maxSeen = new long[8];
        using var stop = new CancellationTokenSource();

        var readers = Enumerable.Range(0, 8).Select(t => Task.Factory.StartNew(
            () =>
            {
                var i = t;
                while (!stop.IsCancellationRequested)
                {
                    var key = _keys[i++ % _keys.Length];
                    if (!store.TryLoad(key, out var version, out var data))
                    {
                        failures.Enqueue($"{key} missing");
                    }
                    else if (!data.AsSpan().SequenceEqual(Data(key, version)))
                    {
                        failures.Enqueue($"{key} version {version} has data of another version");
                    }
                    else
                    {
                        maxSeen[t] = Math.Max(maxSeen[t], version);
                    }
                }
            },
            TaskCreationOptions.LongRunning)).ToArray();

        for (uint version = 2; version <= 40; version++)
        {
            Assert.True(await writer.CheckpointAsync(Checkpoint(version)));
        }

        await stop.CancelAsync();
        await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Empty(failures);
        Assert.All(maxSeen, seen => Assert.True(seen >= 1));
        Assert.True(store.TryLoad(_keys[5], out var last, out _));
        Assert.Equal(40u, last);
    }
}
