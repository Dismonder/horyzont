using GdUnit4;

using Godot;

using Horyzont.Core.Operations;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>K5-22 step 1 / decision 1: the terrain API takes planet-frame coordinates wherever the planet node stands (engine).</summary>
[TestSuite]
public class MultiPlanetTerrainTests
{
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task PlanetTerrain_NodeAt108km_ApisUsePlanetLocalCoordinates()
    {
        var atOrigin = await Probe(Godot.Vector3.Zero);
        var far = await Probe(new Godot.Vector3(108_000, 0, 0));

        GD.Print($"K5-22 node at 108 km: origin {atOrigin}, far {far}");
        AssertThat(far.Loaded).IsTrue();
        AssertThat(Math.Abs(far.HitBefore - atOrigin.HitBefore) < 1e-3).IsTrue();
        AssertThat(Math.Abs(far.HitAfter - atOrigin.HitAfter) < 1e-3).IsTrue();
        AssertThat(far.HitAfter > far.HitBefore + 1f).IsTrue();
        AssertThat(far.Digest).IsEqual(atOrigin.Digest);
    }

    /// <summary>Loads the ground through a global viewer over the node, raycasts, digs and raycasts again — all in planet coordinates.</summary>
    private static async Task<ProbeResult> Probe(Godot.Vector3 nodePosition)
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        world.Node.Position = nodePosition;
        var ground = TerrainTestWorld.Ground(0, 0);

        // A viewer outside the node, in global coordinates: Voxel Tools converts it into the node's space.
        var viewer = new VoxelViewer { ViewDistance = 64, RequiresVisuals = false, RequiresCollisions = false };
        scene.Add(viewer);
        viewer.GlobalPosition = nodePosition + TerrainVectors.ToGodot(ground);
        var loaded = await world.WaitLoaded(ground, 24);

        var origin = ground + new Vector3(0, 20, 0);
        var before = world.Service.Raycast(world.Node.Planet, origin, -Vector3.UnitY, 64f, out var hitBefore) ? hitBefore.Distance : -1f;
        world.Edit(ground, 3f, TerrainEditMode.Remove);
        var after = world.Service.Raycast(world.Node.Planet, origin, -Vector3.UnitY, 64f, out var hitAfter) ? hitAfter.Distance : -1f;
        var digest = world.LiveDigest();
        viewer.QueueFree();
        return new ProbeResult(loaded && world.Service.IsAreaLoaded(world.Node.Planet, ground, 8f), before, after, digest);
    }

    private readonly record struct ProbeResult(bool Loaded, float HitBefore, float HitAfter, ulong Digest);
}
