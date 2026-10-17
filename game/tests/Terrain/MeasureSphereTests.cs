using GdUnit4;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Game.Planet;
using Horyzont.Game.Tests.Support;
using Horyzont.Testing;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>
/// <c>VoxelTerrainService.MeasureSphere</c> over a real <c>VoxelLodTerrain</c> with a flat generator at y = 4000 (K6-29W D9).
/// Voxels sit on the integer grid; SDF &lt; 0 is solid, so the plane y = 4000 (SDF 0) counts as air.
/// </summary>
[TestSuite]
public class MeasureSphereTests
{
    private const float S = TerrainTestWorld.Surface;

    private static int LatticePoints(Vector3 center, float radius, Func<int, bool> includeY)
    {
        var count = 0;
        var r2 = (double)radius * radius;
        for (var x = (int)Math.Ceiling(center.X - radius); x <= (int)Math.Floor(center.X + radius); x++)
        {
            for (var y = (int)Math.Ceiling(center.Y - radius); y <= (int)Math.Floor(center.Y + radius); y++)
            {
                for (var z = (int)Math.Ceiling(center.Z - radius); z <= (int)Math.Floor(center.Z + radius); z++)
                {
                    var d2 = Math.Pow(x - center.X, 2) + Math.Pow(y - center.Y, 2) + Math.Pow(z - center.Z, 2);
                    if (d2 <= r2 && includeY(y))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private static async Task<TerrainTestWorld> LoadedWorldAsync(EngineTestScene scene)
    {
        var world = await TerrainTestWorld.CreateAsync(scene);
        world.AddViewer(TerrainTestWorld.Ground(0, 0), 96);
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 48)).IsTrue();

        // The flat generator writes no material channel (Voxel Tools leaves its raw default, which is no texture): paint the area
        // with the stone texture the way the planet generator does.
        var tool = world.Node.Terrain.GetVoxelTool();
        tool.Channel = TerrainChannels.Material;
        tool.Mode = VoxelTool.ModeEnum.Set;
        tool.Value = TerrainMaterials.StoneTextureIndex;
        tool.DoBox(new Vector3I(-40, (int)S - 40, -40), new Vector3I(40, (int)S + 40, 40));
        tool.Channel = TerrainChannels.Sdf;
        return world;
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Measure_BelowSurface_CountsEverySphereVoxelAsStone()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await LoadedWorldAsync(scene);
        var center = new Vector3(0.3f, S - 2.6f, -0.4f);
        var solid = new int[256];

        var ok = world.Service.MeasureSphere(WorldFactory.HomePlanet, center, 2f, solid, out var air);

        AssertThat(ok).IsTrue();
        var expected = LatticePoints(center, 2f, _ => true);
        AssertThat(expected).IsGreater(20);
        AssertThat(solid[world.Stone]).IsEqual(expected);
        AssertThat(solid.Sum()).IsEqual(expected);
        AssertThat(air).IsEqual(0);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Measure_AboveSurface_CountsAir()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await LoadedWorldAsync(scene);
        var center = new Vector3(-1.2f, S + 3.4f, 0.7f);
        var solid = new int[256];

        var ok = world.Service.MeasureSphere(WorldFactory.HomePlanet, center, 2f, solid, out var air);

        AssertThat(ok).IsTrue();
        AssertThat(solid.Sum()).IsEqual(0);
        AssertThat(air).IsEqual(LatticePoints(center, 2f, _ => true));
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Measure_AcrossSurface_SplitsSolidAndAir()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await LoadedWorldAsync(scene);
        var center = new Vector3(0.2f, S - 0.3f, 0.1f);
        var solid = new int[256];

        var ok = world.Service.MeasureSphere(WorldFactory.HomePlanet, center, 1.5f, solid, out var air);

        AssertThat(ok).IsTrue();
        AssertThat(solid[world.Stone]).IsEqual(LatticePoints(center, 1.5f, y => y < S));
        AssertThat(air).IsEqual(LatticePoints(center, 1.5f, y => y >= S));
        AssertThat(solid[world.Stone]).IsGreater(0);
        AssertThat(air).IsGreater(0);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Measure_AfterAddEdit_ReportsTheMaterialOfTheEdit()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await LoadedWorldAsync(scene);
        var spot = new Vector3(2f, S + 1f, 2f);
        world.Sim.ApplyServerOperation(new TerrainEditOp(WorldFactory.HomePlanet, TerrainShape.Sphere, spot, 2f, TerrainEditMode.Add, world.Sand));
        var solid = new int[256];

        var ok = world.Service.MeasureSphere(WorldFactory.HomePlanet, spot, 1.5f, solid, out var air);

        AssertThat(ok).IsTrue();
        AssertThat(solid[world.Sand]).IsGreater(0);
        AssertThat(solid.Sum() + air).IsEqual(LatticePoints(spot, 1.5f, _ => true));
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Measure_UnloadedAreaOrBadArguments_ReturnsFalseAndCountsNothing()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await LoadedWorldAsync(scene);
        var solid = new int[256];
        solid[1] = 7;

        var far = world.Service.MeasureSphere(WorldFactory.HomePlanet, new Vector3(2000f, S, 0f), 2f, solid, out var airFar);
        var unknown = world.Service.MeasureSphere(new PlanetId(99), Vector3.Zero, 2f, solid, out _);
        var nan = world.Service.MeasureSphere(WorldFactory.HomePlanet, new Vector3(0f, S, 0f), float.NaN, solid, out _);
        var huge = world.Service.MeasureSphere(WorldFactory.HomePlanet, new Vector3(0f, S, 0f), 500f, solid, out _);
        var small = world.Service.MeasureSphere(WorldFactory.HomePlanet, new Vector3(0f, S, 0f), 2f, new int[10], out _);

        AssertThat(far).IsFalse();
        AssertThat(unknown).IsFalse();
        AssertThat(nan).IsFalse();
        AssertThat(huge).IsFalse();
        AssertThat(small).IsFalse();
        AssertThat(airFar).IsEqual(0);
        AssertThat(solid.Sum()).IsEqual(0);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Measure_RadiusEight_DoesNotAllocateManagedMemory()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await LoadedWorldAsync(scene);
        var center = new Vector3(0.3f, S - 1f, 0.3f);
        var solid = new int[256];
        world.Service.MeasureSphere(WorldFactory.HomePlanet, center, 8f, solid, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 5; i++)
        {
            world.Service.MeasureSphere(WorldFactory.HomePlanet, center, 8f, solid, out _);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertThat(allocated).IsLess(2048);
    }
}
