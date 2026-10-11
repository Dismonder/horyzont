using GdUnit4;

using Horyzont.Core.Space;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Surface;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Planet;

/// <summary>K5-22 step 1 / decision 11: the surface sampler reads the generator graph (engine).</summary>
[TestSuite]
public class PlanetSurfaceSamplerTests
{
    private const ulong Seed = 21;

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public void PlanetSurfaceSampler_FastPathMatchesBisection_1000Directions()
    {
        var moon = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current).Bodies[1];
        var graph = GeneratorProfiles.CreateGenerator(moon);
        var sampler = new PlanetSurfaceSampler(moon, graph);
        var random = new Random(5);
        var worst = 0f;
        for (var i = 0; i < 1000; i++)
        {
            var direction = RandomDirection(random);
            var radius = sampler.SurfaceRadius(direction);
            AssertThat(radius >= moon.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - 2f).IsTrue();
            AssertThat(radius <= moon.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + 2f).IsTrue();

            // Reference: one dense ray over the whole relief band (sign of the SDF only, like the bisection of decision 11).
            var up = new Godot.Vector3(direction.X, direction.Y, direction.Z);
            var top = moon.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + 2f;
            var bottom = moon.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - 2f;
            var hit = graph.RaycastSdfApprox(up * top, up * bottom, 0.0625f);
            AssertThat(hit >= 0f).IsTrue();
            worst = MathF.Max(worst, MathF.Abs((top - hit) - radius));
        }

        Godot.GD.Print($"K5-22 surface sampler: worst |dh| = {worst} m over 1000 directions");
        AssertThat(worst <= 0.25f).IsTrue();
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task PlanetSurfaceSampler_OffMainThread_SameHeights()
    {
        var moon = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current).Bodies[1];
        var sampler = new PlanetSurfaceSampler(moon, GeneratorProfiles.CreateGenerator(moon));
        var random = new Random(9);
        var directions = new NumVector3[200];
        for (var i = 0; i < directions.Length; i++)
        {
            directions[i] = RandomDirection(random);
        }

        var main = directions.Select(sampler.SurfaceRadius).ToArray();
        var workers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => directions.Select(sampler.SurfaceRadius).ToArray())));

        foreach (var worker in workers)
        {
            AssertThat(worker.SequenceEqual(main)).IsTrue();
        }
    }

    private static NumVector3 RandomDirection(Random random)
    {
        while (true)
        {
            var v = new NumVector3((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1));
            var length = v.Length();
            if (length is > 0.1f and <= 1f)
            {
                return v / length;
            }
        }
    }
}
