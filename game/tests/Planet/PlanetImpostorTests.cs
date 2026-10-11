using System.Diagnostics;

using GdUnit4;

using Horyzont.Core.Space;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Impostors;
using Horyzont.Game.Planet.Surface;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Planet;

/// <summary>K5-22 decision 12: impostors from the same generator, 6 × 64 × 64 quads, planet-local positions (engine).</summary>
[TestSuite]
public class PlanetImpostorTests
{
    private const ulong Seed = 21;

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public void PlanetImpostorMesh_64PerFace_HeightsMatchSampler()
    {
        var moon = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current).Bodies[1];
        var sampler = new PlanetSurfaceSampler(moon, GeneratorProfiles.CreateGenerator(moon));
        var data = PlanetImpostorMeshBuilder.Build(sampler);

        AssertThat(data.Vertices.Length).IsEqual(6 * 65 * 65);
        AssertThat(data.Indices.Length).IsEqual(6 * 64 * 64 * 2 * 3);
        var low = moon.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - 2f;
        var high = moon.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + 2f;
        foreach (var vertex in data.Vertices)
        {
            // Planet-local: the radius is the distance from the origin of the mesh, never a world position.
            var radius = vertex.Length();
            AssertThat(radius >= low && radius <= high).IsTrue();
        }

        // Every 97th vertex against the precise sampler (impostor strides 4 m / 0.25 m vs 2 m / 0.125 m).
        var worst = 0.0;
        for (var i = 0; i < data.Vertices.Length; i += 97)
        {
            var v = data.Vertices[i];
            var direction = new NumVector3((float)v.X, (float)v.Y, (float)v.Z);
            worst = Math.Max(worst, Math.Abs(v.Length() - sampler.SurfaceRadius(direction)));
        }

        AssertThat(worst <= 0.5).IsTrue();

        // Triangles face outwards (clockwise seen from outside, the Godot front face) and normals point out.
        for (var t = 0; t < data.Indices.Length; t += 3 * 61)
        {
            var a = data.Vertices[data.Indices[t]];
            var b = data.Vertices[data.Indices[t + 1]];
            var c = data.Vertices[data.Indices[t + 2]];
            AssertThat((b - a).Cross(c - a).Dot(a) < 0).IsTrue();
            AssertThat(data.Normals[data.Indices[t]].Dot(a.Normalized()) > 0.2).IsTrue();
        }
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task PlanetImpostor_FourBodies_BuiltInBackgroundUnder2s()
    {
        var bodies = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current).Bodies;
        var samplers = bodies.Select(b => new PlanetSurfaceSampler(b, GeneratorProfiles.CreateGenerator(b))).ToArray();
        var watch = Stopwatch.StartNew();
        var built = await Task.WhenAll(samplers.Select(s => Task.Run(() => PlanetImpostorMeshBuilder.Build(s))));
        watch.Stop();

        Godot.GD.Print($"K5-22 impostors: {built.Length} bodies in {watch.Elapsed.TotalMilliseconds:F0} ms "
            + $"(each {string.Join(", ", built.Select(b => b.BuildMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)))} ms)");
        AssertThat(built.Length).IsEqual(4);
        AssertThat(watch.Elapsed.TotalSeconds < 2.0).IsTrue();
    }
}
