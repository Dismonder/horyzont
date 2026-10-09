using GdUnit4;

using Godot;

using Horyzont.Core.Operations;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Persistence.Terrain;
using Horyzont.Testing;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

[TestSuite]
public class TerrainStreamTests
{
    private const float S = TerrainTestWorld.Surface;

    private static readonly Vector3 _area = TerrainTestWorld.Ground(-20, 20);
    private static readonly Vector3 _away = new(3000, S, 0);

    private static async Task<VoxelViewer> Edited(TerrainTestWorld world)
    {
        var viewer = world.AddViewer(_area);
        AssertThat(await world.WaitLoaded(_area, 24)).IsTrue();
        world.Edit(_area + new Vector3(0.5f, 0, -0.5f), 3f, TerrainEditMode.Remove);
        world.Edit(_area + new Vector3(-7f, 1, 2f), 2.5f, TerrainEditMode.Add);
        world.Edit(new Vector3(-16f, S - 0.01f, 16f), 1.5f, TerrainEditMode.Remove);
        return viewer;
    }

    private static async Task UnloadAndReload(TerrainTestWorld world, VoxelViewer viewer)
    {
        viewer.Position = TerrainVectors.ToGodot(_away);
        AssertThat(await world.WaitUnloaded(_area, 24)).IsTrue();
        viewer.Position = TerrainVectors.ToGodot(_area);
        AssertThat(await world.WaitLoaded(_area, 24)).IsTrue();
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Stream_UnloadReloadBeforeCheckpoint_ReturnsPendingData()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        var viewer = await Edited(world);
        var live = world.LiveDigest();
        var reads = world.Node.Stream.SourceReads;

        await UnloadAndReload(world, viewer);

        AssertThat(world.Blocks.PendingCount).IsEqual(world.State.TerrainVersions.All.Count());
        AssertThat(world.Node.Stream.SourceReads).IsGreater(reads);
        AssertThat(world.LiveDigest()).IsEqual(live);
        AssertThat(world.Node.Stream.ErrorCount).IsEqual(0);
        AssertThat(world.Node.Stream.SaveMismatches).IsEqual(0);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Stream_UnloadReloadAfterCommit_ReturnsStoreData()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        var viewer = await Edited(world);
        var live = world.LiveDigest();
        AssertThat(await world.CheckpointAndPump()).IsTrue();
        AssertThat(world.Blocks.PendingCount).IsEqual(0);

        await UnloadAndReload(world, viewer);

        AssertThat(world.LiveDigest()).IsEqual(live);
        AssertThat(world.Node.Stream.ErrorCount).IsEqual(0);
        AssertThat(world.Node.Stream.SaveMismatches).IsEqual(0);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Checkpoint_BlocksUnloadedBeforeCapture_AreSaved()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        var viewer = await Edited(world);
        var expected = world.State.TerrainVersions.All.ToDictionary(v => v.Key, v => v.Version);
        var blobs = expected.Keys.ToDictionary(k => k, k => world.Blocks.TryLoad(k, out _, out var blob) == TerrainBlockLookup.Found ? blob : []);
        viewer.Position = TerrainVectors.ToGodot(_away);
        AssertThat(await world.WaitUnloaded(_area, 24)).IsTrue();

        AssertThat(await world.CheckpointAndPump()).IsTrue();

        var payload = world.Requests[^1].TerrainBlocks;
        AssertThat(payload.Count).IsEqual(expected.Count);
        foreach (var block in payload)
        {
            AssertThat(block.Version).IsEqual(expected[block.Key]);
            AssertThat(world.Store.TryLoad(block.Key, out var version, out var data)).IsTrue();
            AssertThat(version).IsEqual(expected[block.Key]);
            AssertThat(data).IsEqual(blobs[block.Key]);
        }
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Shutdown_StreamCallsAfterClose_ReturnErrorWithoutStore()
    {
        await using var scene = EngineTestScene.Create();
        var world = await TerrainTestWorld.CreateAsync(scene);
        await Edited(world);
        var key = world.State.TerrainVersions.All.First().Key;
        var stream = world.Node.Stream;
        await world.Writer.DisposeAsync();

        AssertThat(await world.Service.ShutdownAsync()).IsTrue();
        world.Store.Close();
        var reads = stream.SourceReads;
        using var buffer = new VoxelBuffer();
        buffer.Create(16, 16, 16);
        var load = stream._LoadVoxelBlock(buffer, new Vector3I(key.X, key.Y, key.Z), 0);
        stream._SaveVoxelBlock(buffer, new Vector3I(key.X, key.Y, key.Z), 0);
        stream._SaveVoxelBlock(buffer, new Vector3I(key.X >> 1, key.Y >> 1, key.Z >> 1), 1);
        var lod1 = stream._LoadVoxelBlock(buffer, new Vector3I(key.X >> 1, key.Y >> 1, key.Z >> 1), 1);

        AssertThat(load).IsEqual((int)VoxelStream.ResultCode.Error);
        AssertThat(lod1).IsEqual((int)VoxelStream.ResultCode.Error);
        AssertThat(stream.SourceReads).IsEqual(reads);
        AssertThat(world.Blocks.TryLoad(key, out _, out _)).IsEqual(TerrainBlockLookup.Unavailable);
        AssertThat(world.Service.IsAreaLoaded(WorldFactory.HomePlanet, _area, 1f)).IsFalse();

        // Freeing the terrain with the closed stream and the closed store must not reach SQLite from Voxel Tools' threads.
        world.Node.GetParent()?.RemoveChild(world.Node);
        world.Node.QueueFree();
        await scene.WaitFrames(5);
        AssertThat(stream.ErrorCount).IsEqual(0);
        world.Service.Dispose();
    }
}
