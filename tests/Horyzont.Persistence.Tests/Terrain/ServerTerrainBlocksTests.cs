using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests.Terrain;

[Collection(SqlitePools.Name)]
public sealed class ServerTerrainBlocksTests
{
    private static readonly TerrainBlockKey _a = new(WorldFactory.HomePlanet, 0, 0, 250, 0);
    private static readonly TerrainBlockKey _b = new(WorldFactory.HomePlanet, 0, -1, 249, -1);
    private static readonly TerrainBlockKey _c = new(WorldFactory.HomePlanet, 0, 3, -250, 7);

    internal static byte[] Blob(TerrainBlockKey key, uint version) =>
        StateCodec.EncodeTerrainBlockBlob(1, [(byte)key.X, (byte)key.Y, (byte)key.Z, (byte)version, (byte)(version >> 8)]);

    internal static WorldCheckpoint Checkpoint(params TerrainBlockVersion[] versions) =>
        new(OpId.None, new Tick(1), 1, 1, [], [], [], [], [], versions);

    internal static ServerTerrainBlocks NewBlocks(TerrainBlockStore store, IEnumerable<TerrainBlockVersion>? saved = null, TerrainBufferOptions? options = null, TimeProvider? time = null) =>
        new(store, saved ?? [], options ?? new TerrainBufferOptions(), time ?? new FakeTimeProvider(), NullLogger<ServerTerrainBlocks>.Instance);

    /// <summary>An empty world file with the given block rows saved by a checkpoint.</summary>
    private static async Task CreateWorld(TempDirectory dir, params TerrainBlockVersion[] rows)
    {
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());
        await using var writer = new PersistenceWriter(
            WorldDatabase.Open(dir.WorldPath), WorldHarness.Options, new FakeTimeProvider(), Faults.NoFaults.Instance,
            NullLogger<PersistenceWriter>.Instance);
        Assert.True(await writer.CheckpointAsync(new CheckpointRequest(Checkpoint(rows), [.. rows.Select(r => new TerrainBlockPayload(r.Key, r.Version, Blob(r.Key, r.Version)))])));
    }

    [Fact]
    public async Task TryLoad_PendingEntry_WinsOverStore()
    {
        using var dir = new TempDirectory();
        await CreateWorld(dir, new TerrainBlockVersion(_a, 1));
        using var store = new TerrainBlockStore(dir.WorldPath);
        var blocks = NewBlocks(store, [new TerrainBlockVersion(_a, 1)]);

        Assert.Equal(TerrainBlockLookup.Found, blocks.TryLoad(_a, out var saved, out var savedBlob));
        Assert.Equal(1u, saved);
        Assert.Equal(Blob(_a, 1), savedBlob);
        Assert.Equal(1, store.QueryCount);

        blocks.Record(_a, 2, Blob(_a, 2));

        Assert.Equal(TerrainBlockLookup.Found, blocks.TryLoad(_a, out var pending, out var pendingBlob));
        Assert.Equal(2u, pending);
        Assert.Equal(Blob(_a, 2), pendingBlob);
        Assert.Equal(1, store.QueryCount);
    }

    [Fact]
    public async Task TryLoad_NeverEdited_NotEditedWithoutQuery()
    {
        using var dir = new TempDirectory();
        await CreateWorld(dir, new TerrainBlockVersion(_a, 1));
        using var store = new TerrainBlockStore(dir.WorldPath);
        var blocks = NewBlocks(store, [new TerrainBlockVersion(_a, 1)]);

        Assert.Equal(TerrainBlockLookup.NotEdited, blocks.TryLoad(_b, out var version, out var blob));
        Assert.Equal(TerrainBlockLookup.NotEdited, blocks.TryLoad(_a with { Lod = 1 }, out _, out _));

        Assert.Equal(0u, version);
        Assert.Empty(blob);
        Assert.Equal(0, store.QueryCount);
    }

    [Fact]
    public async Task TryLoad_VersionWithoutRow_UnavailableAndFirstCorruptKey()
    {
        using var dir = new TempDirectory();
        await CreateWorld(dir, new TerrainBlockVersion(_a, 1));
        using var store = new TerrainBlockStore(dir.WorldPath);
        var blocks = NewBlocks(store, [new TerrainBlockVersion(_a, 1), new TerrainBlockVersion(_b, 3), new TerrainBlockVersion(_c, 2)]);
        Assert.Null(blocks.FirstCorruptKey);

        Assert.Equal(TerrainBlockLookup.Unavailable, blocks.TryLoad(_b, out var version, out var blob));
        Assert.Equal(TerrainBlockLookup.Unavailable, blocks.TryLoad(_c, out _, out _));

        Assert.Equal(0u, version);
        Assert.Empty(blob);
        Assert.Equal(_b, blocks.FirstCorruptKey);
        Assert.Equal(TerrainBlockLookup.Found, blocks.TryLoad(_a, out _, out _));
    }

    [Fact]
    public async Task Record_VersionNotIncreasing_Throws()
    {
        using var dir = new TempDirectory();
        await CreateWorld(dir);
        using var store = new TerrainBlockStore(dir.WorldPath);
        var blocks = NewBlocks(store, [new TerrainBlockVersion(_c, 3)]);
        blocks.Record(_a, 1, Blob(_a, 1));

        Assert.Throws<InvalidOperationException>(() => blocks.Record(_a, 1, Blob(_a, 1)));
        Assert.Throws<InvalidOperationException>(() => blocks.Record(_b, 0, Blob(_b, 0)));
        Assert.Throws<InvalidOperationException>(() => blocks.Record(_c, 3, Blob(_c, 3)));
        Assert.Throws<InvalidOperationException>(() => blocks.Record(_c, 2, Blob(_c, 2)));
        blocks.Record(_c, 4, Blob(_c, 4));
        blocks.Record(_a, 3, Blob(_a, 3));
        Assert.Equal(2, blocks.PendingCount);
    }

    [Fact]
    public async Task CreateCheckpointRequest_ExactlyCapturedVersions_WriterAcceptsIt()
    {
        using var dir = new TempDirectory();
        await using var server = TerrainServer.CreateNew(dir.WorldPath);
        server.Sim.ApplyServerOperation(Dig(new Vector3(0.5f, 3999.5f, 0.5f)));
        server.Sim.ApplyServerOperation(Dig(new Vector3(20f, 3999f, -20f)));
        server.Sim.ApplyServerOperation(Dig(new Vector3(0.5f, 3999.5f, 0.5f)));

        var core = server.State.CaptureCheckpoint();
        var request = server.Blocks.CreateCheckpointRequest(core);

        Assert.Equal(core.TerrainVersions, request.TerrainBlocks.Select(p => new TerrainBlockVersion(p.Key, p.Version)));
        Assert.All(request.TerrainBlocks, p => Assert.Same(server.Recorded[(p.Key, p.Version)], p.Data));
        Assert.True(await server.Writer.CheckpointAsync(request));
        server.Pump();
        Assert.Equal(0, server.Blocks.PendingCount);
        foreach (var version in core.TerrainVersions)
        {
            Assert.True(server.Store.TryLoad(version.Key, out var saved, out var data));
            Assert.Equal(version.Version, saved);
            Assert.Equal(server.Recorded[(version.Key, version.Version)], data);
        }
    }

    [Fact]
    public async Task CreateCheckpointRequest_NoPendingEntryOfThatVersion_Throws()
    {
        using var dir = new TempDirectory();
        await CreateWorld(dir);
        using var store = new TerrainBlockStore(dir.WorldPath);
        var blocks = NewBlocks(store);
        blocks.Record(_a, 1, Blob(_a, 1));
        blocks.Record(_a, 2, Blob(_a, 2));

        Assert.Throws<InvalidOperationException>(() => blocks.CreateCheckpointRequest(Checkpoint(new TerrainBlockVersion(_a, 1))));
        Assert.Throws<InvalidOperationException>(() => blocks.CreateCheckpointRequest(Checkpoint(new TerrainBlockVersion(_b, 1))));
        Assert.Single(blocks.CreateCheckpointRequest(Checkpoint(new TerrainBlockVersion(_a, 2))).TerrainBlocks);
    }

    [Fact]
    public async Task OnCheckpointCommitted_RemovesOnlyEqualVersions_EditAfterBarrierStays()
    {
        using var dir = new TempDirectory();
        await using var server = TerrainServer.CreateNew(dir.WorldPath);
        server.Sim.ApplyServerOperation(Dig(new Vector3(0.5f, 3999.5f, 0.5f)));
        server.Sim.ApplyServerOperation(Dig(new Vector3(40f, 3999f, 40f)));
        var core = server.State.CaptureCheckpoint();
        var request = server.Blocks.CreateCheckpointRequest(core);

        server.Sim.ApplyServerOperation(Dig(new Vector3(0.5f, 3999.5f, 0.5f)));
        var after = server.Terrain.AppliedVersions[^1];
        Assert.True(await server.Writer.CheckpointAsync(request));
        server.Pump();

        Assert.Equal(after.Count, server.Blocks.PendingCount);
        foreach (var version in after)
        {
            Assert.Equal(TerrainBlockLookup.Found, server.Blocks.TryLoad(version.Key, out var pending, out _));
            Assert.Equal(version.Version, pending);
        }

        var queries = server.Store.QueryCount;
        foreach (var version in server.Terrain.AppliedVersions[1])
        {
            Assert.Equal(TerrainBlockLookup.Found, server.Blocks.TryLoad(version.Key, out var saved, out _));
            Assert.Equal(1u, saved);
        }

        Assert.Equal(queries + server.Terrain.AppliedVersions[1].Count, server.Store.QueryCount);
    }

    [Fact]
    public async Task OnCheckpointFailed_RemovesNothing_NextCheckpointTakesTheBlocks()
    {
        using var dir = new TempDirectory();
        var faults = ScriptedFaults.DiskErrorAt(Faults.FaultPoint.CheckpointMidTransaction);
        await using var server = TerrainServer.CreateNew(dir.WorldPath, faults);
        server.FaultsExpected = true;
        server.Sim.ApplyServerOperation(Dig(new Vector3(0.5f, 3999.5f, 0.5f)));
        var pending = server.Blocks.PendingCount;

        Assert.False(await server.CheckpointAndPump());

        Assert.Equal(pending, server.Blocks.PendingCount);
        server.Writer.ClearFault();
        Assert.True(await server.CheckpointAndPump());
        Assert.Equal(0, server.Blocks.PendingCount);
        Assert.All(server.Terrain.AppliedVersions[0], v => Assert.True(server.Store.TryLoad(v.Key, out _, out _)));
    }

    internal static TerrainEditOp Dig(Vector3 center, float radius = 1f) =>
        new(WorldFactory.HomePlanet, TerrainShape.Sphere, center, radius, TerrainEditMode.Remove, 0);
}
