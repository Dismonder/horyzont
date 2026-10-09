using System.Diagnostics;

using GdUnit4;

using Godot;

using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Persistence.Terrain;
using Horyzont.Testing;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

[TestSuite]
public class TerrainSemanticsTests
{
    /// <summary>Semantics version the golden digest below was recorded for.</summary>
    private const ushort RecordedSemanticsVersion = 1;

    /// <summary>
    /// Golden digest of <see cref="GoldenEdits"/> on the flat Voxel Tools generator. A different value means the bytes of an edit
    /// changed (constants of <see cref="TerrainEditSemantics"/>, Voxel Tools or the material map): bump
    /// <see cref="TerrainEditSemantics.SemanticsVersion"/>, re-record both constants and describe it in the PR (ZASADY point 3).
    /// </summary>
    private const ulong GoldenDigest = 0x915455B1D651C6C8UL;

    private const float S = TerrainTestWorld.Surface;

    /// <summary>50 fixed edits: add and remove, every material, radii 0.5–3 m, block faces, edges, corners and negative coordinates.</summary>
    private static List<(Vector3 Center, float Radius, TerrainEditMode Mode, string Material)> GoldenEdits()
    {
        string[] materials = [TerrainMaterials.StoneKey, TerrainMaterials.DirtKey, TerrainMaterials.SandKey, TerrainMaterials.OreKey, TerrainMaterials.CoreMaterialKey];
        var edits = new List<(Vector3, float, TerrainEditMode, string)>(50);
        for (var i = 0; i < 50; i++)
        {
            // Exact float arithmetic (sums and products of small binary fractions): the list is the same on every platform.
            var x = ((i * 7) % 33) - 16 + ((i % 3) * 0.25f);
            var y = S + ((i % 5) - 2) + ((i % 4) * 0.125f);
            var z = ((i * 11) % 29) - 14 - ((i % 2) * 0.5f);
            if (i % 10 == 0)
            {
                x = MathF.Round(x / 16f) * 16f;
                z = MathF.Round(z / 16f) * 16f;
            }

            var radius = 0.5f + ((i % 6) * 0.5f);
            var mode = i % 3 == 1 ? TerrainEditMode.Remove : TerrainEditMode.Add;
            edits.Add((new Vector3(x, y, z), radius, mode, materials[i % materials.Length]));
        }

        return edits;
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task EditSemantics_GoldenDigest()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        world.AddViewer(TerrainTestWorld.Ground(0, 0), 96);
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 40)).IsTrue();

        foreach (var (center, radius, mode, material) in GoldenEdits())
        {
            world.State.Materials.TryGetId(material, out var id);
            world.Sim.ApplyServerOperation(
                new TerrainEditOp(WorldFactory.HomePlanet, TerrainShape.Sphere, center, radius, mode, mode == TerrainEditMode.Add ? id : (byte)0));
        }

        var digest = world.LiveDigest();
        GD.Print($"EditSemantics_GoldenDigest semantics={TerrainEditSemantics.SemanticsVersion} digest=0x{digest:X16}UL blocks={world.State.TerrainVersions.All.Count()}");
        AssertThat(TerrainEditSemantics.SemanticsVersion).IsEqual(RecordedSemanticsVersion);
        AssertThat($"0x{digest:X16}").IsEqual($"0x{GoldenDigest:X16}");
    }

    [TestCase(Timeout = 600000)]
    [TestCategory("Slow")]
    [RequireGodotRuntime]
    public async Task ApplyEdit_Radius3_TimeBudget()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene, new TerrainBufferOptions { EarlyCheckpointBlocks = 100000, MaxPendingBlocks = 100000 });
        world.AddViewer(TerrainTestWorld.Ground(0, 0), 96);
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 40)).IsTrue();
        var keys = new List<TerrainBlockKey>();
        var versions = new Dictionary<TerrainBlockKey, uint>();
        var times = new List<double>();
        var random = new Random(9);
        for (var i = 0; i < 520; i++)
        {
            // Centres just off a block corner, so every edit copies and encodes 2 x 2 x 2 = 8 blocks.
            var center = new Vector3((random.Next(-1, 2) * 16) + 0.3f, S + 0.2f, (random.Next(-1, 2) * 16) - 0.4f);
            var op = new TerrainEditOp(WorldFactory.HomePlanet, TerrainShape.Sphere, center, 3f, i % 2 == 0 ? TerrainEditMode.Remove : TerrainEditMode.Add, world.Stone);
            TerrainMath.AffectedBlocks(WorldFactory.HomePlanet, center, 3f, 1f, TerrainRules.EditMarginVoxels, keys);
            AssertThat(keys.Count).IsEqual(8);
            var list = new List<TerrainBlockVersion>(keys.Count);
            foreach (var key in keys)
            {
                versions[key] = versions.GetValueOrDefault(key) + 1;
                list.Add(new TerrainBlockVersion(key, versions[key]));
            }

            var started = Stopwatch.GetTimestamp();
            world.Service.ApplyEdit(op, list);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (i >= 20)
            {
                times.Add(elapsed);
            }
        }

        times.Sort();
        var median = times[times.Count / 2];
        var p99 = times[(int)(times.Count * 0.99)];
        var report = $"ApplyEdit_Radius3_TimeBudget edits={times.Count} median={median:F3} ms p99={p99:F3} ms max={times[^1]:F3} ms";
        GD.Print(report);
        if (System.Environment.GetEnvironmentVariable("HORYZONT_PERF_OUT") is { Length: > 0 } perfOut)
        {
            File.AppendAllText(perfOut, report + System.Environment.NewLine);
        }

        AssertThat(median).IsLessEqual(1.0);
        AssertThat(p99).IsLessEqual(3.0);
    }
}
