using System.Numerics;

using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Constructs;
using Horyzont.Game.Tests.Constructs;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Blocks;

/// <summary>
/// K6-30 stage C: doors, lamps and chests in the real construct world (headless, no graphics): an open door has no collision and is
/// drawn as a thin panel, a lit lamp gets a light (at most 32, nearest to the camera, without shadows) and a compound operation reaches
/// the view through <c>OperationConstructs</c>. How it looks is for Dismond to judge.
/// </summary>
[TestSuite]
public class FunctionalBlocksTests
{
    private const string Door = "core:wood_door";
    private const string Lamp = "core:lamp";

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task Door_OpenedByFunctionState_LosesItsCollisionAndKeepsAMesh_ClosedHasBoth()
    {
        await using var scene = EngineTestScene.Create();
        using var w = new ConstructTestWorld(scene.Root, ConstructTestWorld.NewState(BlockContentLoader.Load()));
        var id = w.Create(new CellPos(0, 0, 0), Door);
        await Settle(scene, w);

        var closed = Chunk(w, id);
        AssertThat(closed.Body is not null).IsTrue();
        AssertThat(closed.CollisionTriangleCount).IsEqual(12);

        Toggle(w, id, new CellPos(0, 0, 0), Door, 1);
        await Settle(scene, w);

        var open = Chunk(w, id);
        AssertThat(open.Body is null).IsTrue();
        AssertThat(open.CollisionTriangleCount).IsEqual(0);
        AssertThat(open.MeshInstance is not null).IsTrue();

        Toggle(w, id, new CellPos(0, 0, 0), Door, 0);
        await Settle(scene, w);

        AssertThat(Chunk(w, id).CollisionTriangleCount).IsEqual(12);
    }

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task DoorColumn_OneCompoundOfFunctionStates_RebuildsTheChunk()
    {
        await using var scene = EngineTestScene.Create();
        using var w = new ConstructTestWorld(scene.Root, ConstructTestWorld.NewState(BlockContentLoader.Load()));
        var id = w.Create(new CellPos(0, 0, 0), Door);
        w.Set(id, new CellPos(0, 1, 0), Door);
        await Settle(scene, w);
        AssertThat(Chunk(w, id).CollisionTriangleCount > 0).IsTrue();

        var type = w.TypeId(Door);
        w.Apply(new CompoundOp([
            new BlockFunctionStateSetOp(id, new CellPos(0, 0, 0), type, 1),
            new BlockFunctionStateSetOp(id, new CellPos(0, 1, 0), type, 1)]));
        await Settle(scene, w);

        AssertThat(Chunk(w, id).Body is null).IsTrue();
        AssertThat(Chunk(w, id).CollisionTriangleCount).IsEqual(0);

        w.Apply(new CompoundOp([
            new BlockFunctionStateSetOp(id, new CellPos(0, 0, 0), type, 0),
            new BlockFunctionStateSetOp(id, new CellPos(0, 1, 0), type, 0)]));
        await Settle(scene, w);

        AssertThat(Chunk(w, id).CollisionTriangleCount > 0).IsTrue();
    }

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task CompoundWithConstructCreate_LoadsTheNewConstruct()
    {
        await using var scene = EngineTestScene.Create();
        using var w = new ConstructTestWorld(scene.Root, ConstructTestWorld.NewState(BlockContentLoader.Load()));
        var id = w.State.EntityIds.PeekNext();

        w.Apply(new CompoundOp([
            new ConstructCreateOp(id, ConstructTestWorld.Planet, new Vector3(0, 4000, 0), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(w.TypeId(Door), 0))]));
        await Settle(scene, w);

        AssertThat(w.World.TryGetConstruct(id, out _)).IsTrue();
        AssertThat(Chunk(w, id).CollisionTriangleCount).IsEqual(12);
    }

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task Lamp_LitHasAShadowlessLight_SwitchedOffHasNone()
    {
        await using var scene = EngineTestScene.Create();
        using var w = new ConstructTestWorld(scene.Root, ConstructTestWorld.NewState(BlockContentLoader.Load()));
        var lamp = w.TypeId(Lamp);
        var id = w.State.EntityIds.PeekNext();
        w.Apply(new ConstructCreateOp(id, ConstructTestWorld.Planet, new Vector3(0, 4000, 0), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(lamp, 0).WithFunctionState(1)));
        await Settle(scene, w);

        var lights = w.World.Lights!;
        lights.Update(new Godot.Vector3(0, 4000.5f, 0));
        AssertThat(lights.ActiveLights.Count).IsEqual(1);
        var light = lights.ActiveLights[0];
        AssertThat(light.ShadowEnabled).IsFalse();
        AssertThat(light.GlobalPosition.DistanceTo(new Godot.Vector3(0.5f, 4000.5f, 0.5f)) < 0.01f).IsTrue();
        AssertThat(light.OmniRange > 0f).IsTrue();

        Toggle(w, id, new CellPos(0, 0, 0), Lamp, 0);
        await Settle(scene, w);
        lights.Update(new Godot.Vector3(0, 4000.5f, 0));
        AssertThat(lights.ActiveLights.Count).IsEqual(0);
        AssertThat(lights.VisibleLightCount).IsEqual(0);

        Toggle(w, id, new CellPos(0, 0, 0), Lamp, 1);
        await Settle(scene, w);
        lights.Update(new Godot.Vector3(0, 4000.5f, 0));
        AssertThat(lights.ActiveLights.Count).IsEqual(1);
    }

    [TestCase(Timeout = 180000)]
    [RequireGodotRuntime]
    public async Task Lamps_FortyLit_OnlyThe32NearestToTheCameraGetALight()
    {
        await using var scene = EngineTestScene.Create();
        using var w = new ConstructTestWorld(scene.Root, ConstructTestWorld.NewState(BlockContentLoader.Load()));
        var lamp = w.TypeId(Lamp);
        var id = w.State.EntityIds.PeekNext();
        w.Apply(new ConstructCreateOp(id, ConstructTestWorld.Planet, new Vector3(0, 4000, 0), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(lamp, 0).WithFunctionState(1)));
        for (var x = 1; x < 40; x++)
        {
            w.Apply(new BlockSetOp(id, new CellPos(x, 0, 0), BlockState.Create(lamp, 0).WithFunctionState(1)));
        }

        await Settle(scene, w);

        var lights = w.World.Lights!;
        lights.Update(new Godot.Vector3(0, 4000.5f, 0.5f));
        AssertThat(BlockLightsLimit.Value).IsEqual(32);
        AssertThat(lights.ActiveLights.Count).IsEqual(32);
        AssertThat(lights.ActiveLights.Count(l => l.Visible)).IsEqual(32);
        AssertThat(lights.ActiveLights.Max(l => l.GlobalPosition.X) < 32f).IsTrue();
        AssertThat(lights.ActiveLights.Any(l => l.ShadowEnabled)).IsFalse();

        // Moving the camera to the other end swaps the set: the nearest 32 are now the cells 8..39.
        lights.Update(new Godot.Vector3(40f, 4000.5f, 0.5f));
        AssertThat(lights.ActiveLights.Count).IsEqual(32);
        AssertThat(lights.ActiveLights.Min(l => l.GlobalPosition.X) > 7f).IsTrue();
        AssertThat(lights.VisibleLightCount).IsEqual(32);
    }

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task BlockMeshFactory_OpenDoor_IsAThinPanel_ClosedDoorIsACube()
    {
        await using var scene = EngineTestScene.Create();
        using var w = new ConstructTestWorld(scene.Root, ConstructTestWorld.NewState(BlockContentLoader.Load()));
        var factory = new BlockMeshFactory(w.Table);
        var door = w.TypeId(Door);

        var closed = factory.CreateBlockMesh(door, 0)!;
        var open = factory.CreateBlockMesh(door, 0, functionState: 1)!;

        AssertThat(Math.Abs(closed.GetAabb().Size.X - 1f) < 0.001f).IsTrue();
        AssertThat(Math.Abs(open.GetAabb().Size.X - 0.1f) < 0.001f).IsTrue();
        AssertThat(Math.Abs(open.GetAabb().Size.Y - 1f) < 0.001f).IsTrue();
    }

    private static async Task Settle(EngineTestScene scene, ConstructTestWorld w) =>
        AssertThat(await scene.WaitFramesUntil(() => w.World.PendingJobs == 0, 600)).IsTrue();

    private static ConstructChunkNode Chunk(ConstructTestWorld w, EntityId id)
    {
        AssertThat(w.World.TryGetConstruct(id, out var node)).IsTrue();
        AssertThat(node!.TryGetChunk(new ChunkPos(0, 0, 0), out var chunk)).IsTrue();
        return chunk!;
    }

    private static void Toggle(ConstructTestWorld w, EntityId id, CellPos cell, string key, byte state) =>
        w.Apply(new BlockFunctionStateSetOp(id, cell, w.TypeId(key), state));

    private static class BlockLightsLimit
    {
        public static int Value => Horyzont.Game.Client.Blocks.BlockLights.MaxLitLampsRendered;
    }
}
