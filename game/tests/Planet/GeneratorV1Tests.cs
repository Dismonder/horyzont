using GdUnit4;

using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Generators;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

[TestSuite]
public class GeneratorV1Tests
{
    private const ulong GalaxySeed = 0x484F52595A4F4E54UL;

    private static PlanetParams Planet(ulong galaxySeed = GalaxySeed) =>
        PlanetGenesis.CreatePlanet(galaxySeed, PlanetGenesis.HomePlanetIndex, GeneratorProfiles.Current);

    private static (int X, int Y, int Z) PointAt((float X, float Y, float Z) direction, float radius) =>
        ((int)MathF.Floor(direction.X * radius), (int)MathF.Floor(direction.Y * radius), (int)MathF.Floor(direction.Z * radius));

    /// <summary>Walks outward-in along a ray and returns the radius of the outermost solid voxel (the surface), or -1.</summary>
    private static float FindSurface(GeneratorProbe probe, (float X, float Y, float Z) direction, float from, float to, float step)
    {
        for (var t = from; t >= to; t -= step)
        {
            var (x, y, z) = PointAt(direction, t);
            if (probe.Sample(x, y, z).Sdf < 0f)
            {
                return t;
            }
        }

        return -1f;
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_SameSeedTwice_IdenticalBlocks()
    {
        var planet = Planet();
        var samples = GeneratorSampler.Create(planet);
        using var first = new GeneratorDigest(GeneratorProfiles.CreateGenerator(planet));
        using var second = new GeneratorDigest(GeneratorProfiles.CreateGenerator(planet));

        var a = samples.Select(first.Compute).ToArray();
        var b = samples.Select(second.Compute).ToArray();

        AssertThat(samples.Count).IsGreater(2000);
        AssertThat(a).IsEqual(b);
        AssertThat(GeneratorDigest.Combine(a)).IsEqual(GeneratorDigest.Combine(b));
    }

    /// <summary>
    /// Frozen output of <c>core:planet/1</c> (ZASADY point 3): any change of this value means a new profile
    /// (<c>core:planet/2</c>) next to this one, never an edit of <see cref="PlanetGeneratorV1"/>.
    /// </summary>
    private const ulong GeneratorV1Golden = 0x8CDC2388E671FC17UL;

    /// <summary>Frozen digest of the whole sample set (all LODs, uniform blocks included: core, caves, far LODs).</summary>
    private const ulong GeneratorV1GoldenAllSamples = 0x7AE95C60EDF5476EUL;

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_GoldenDigest()
    {
        var planet = Planet();
        using var digest = new GeneratorDigest(GeneratorProfiles.CreateGenerator(planet));
        var golden = GeneratorSampler.Create(planet)
            .Where(s => s.Lod == 0)
            .Select(digest.Compute)
            .Where(d => !d.SdfUniform)
            .Take(128)
            .ToArray();

        AssertThat(golden.Length).IsEqual(128);
        var combined = GeneratorDigest.Combine(golden);
        AssertThat(combined).OverrideFailureMessage($"core:planet/1 digest 0x{combined:X16} != golden 0x{GeneratorV1Golden:X16}").IsEqual(GeneratorV1Golden);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_GoldenDigest_AllSamples()
    {
        var planet = Planet();
        using var digest = new GeneratorDigest(GeneratorProfiles.CreateGenerator(planet));
        var all = GeneratorSampler.Create(planet).Select(digest.Compute).ToArray();

        AssertThat(all.Length).IsGreater(2000);
        var combined = GeneratorDigest.Combine(all);
        AssertThat(combined).OverrideFailureMessage($"core:planet/1 all-samples digest 0x{combined:X16} != golden 0x{GeneratorV1GoldenAllSamples:X16}").IsEqual(GeneratorV1GoldenAllSamples);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_DifferentSeed_DifferentBlocks()
    {
        var planetA = Planet();
        var planetB = Planet(GalaxySeed + 1);
        var samples = GeneratorSampler.Create(planetA).Where(s => s.Lod == 0).Take(1500).ToArray();
        using var digestA = new GeneratorDigest(GeneratorProfiles.CreateGenerator(planetA));
        using var digestB = new GeneratorDigest(GeneratorProfiles.CreateGenerator(planetB));

        var surfaceBlocks = 0;
        var different = 0;
        foreach (var sample in samples)
        {
            var a = digestA.Compute(sample);
            if (a.SdfUniform)
            {
                continue;
            }

            surfaceBlocks++;
            if (a != digestB.Compute(sample))
            {
                different++;
            }
        }

        AssertThat(surfaceBlocks).IsGreater(100);
        AssertThat(different).IsGreater(surfaceBlocks * 9 / 10);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_GraphUsesOnlyAllowedNodes()
    {
        var generator = GeneratorProfiles.CreateGenerator(Planet());
        var function = generator.GetMainFunction();

        AssertThat(GraphBuilder.FindViolations(function)).IsEmpty();
        var noiseNodes = function.GetNodeIds().Count(id => function.GetNodeTypeId((uint)id) == VoxelGraphFunction.NodeTypeId.FastNoise3D);
        AssertThat(noiseNodes).IsEqual(5);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_SurfaceWithinRelief()
    {
        var planet = Planet();
        using var probe = new GeneratorProbe(GeneratorProfiles.CreateGenerator(planet));
        var r = planet.RadiusMeters;

        foreach (var direction in GeneratorSampler.Directions())
        {
            var surface = FindSurface(probe, direction, r + 330f, r - 330f, 2f);

            AssertThat(surface).IsBetween(r - PlanetGeneratorV1.MaxReliefMeters - 3f, r + PlanetGeneratorV1.MaxReliefMeters + 3f);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_CoreSolidAndBedrock()
    {
        var planet = Planet();
        using var probe = new GeneratorProbe(GeneratorProfiles.CreateGenerator(planet));
        var coreRadius = planet.RadiusMeters - planet.CoreDepthMeters;
        var checkedPoints = 0;

        foreach (var direction in GeneratorSampler.Directions())
        {
            foreach (var radius in new[] { 0f, 5f, 100f, 1000f, 2500f, coreRadius - 20f, coreRadius - 1f })
            {
                var (x, y, z) = PointAt(direction, radius);
                var (sdf, material) = probe.Sample(x, y, z);

                AssertThat(sdf).IsLess(0f);
                AssertThat((int)material).IsEqual(TerrainMaterials.CoreTextureIndex);
                checkedPoints++;
            }
        }

        AssertThat(checkedPoints).IsEqual(GeneratorSampler.DirectionCount * 7);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_SpaceEmpty()
    {
        var planet = Planet();
        using var probe = new GeneratorProbe(GeneratorProfiles.CreateGenerator(planet));

        foreach (var direction in GeneratorSampler.Directions())
        {
            foreach (var radius in new[] { planet.RadiusMeters + 316f, planet.RadiusMeters + 500f, planet.RadiusMeters + 1500f })
            {
                var (x, y, z) = PointAt(direction, radius);

                AssertThat(probe.Sample(x, y, z).Sdf).IsGreater(0f);
            }
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_AllMaterialsPresent()
    {
        var planet = Planet();
        var generator = GeneratorProfiles.CreateGenerator(planet);
        var buffer = new VoxelBuffer();
        using var format = TerrainChannels.CreateFormat();
        var seen = new HashSet<byte>();

        foreach (var sample in GeneratorSampler.Create(planet).Where(s => s.Lod == 0))
        {
            buffer.Create(16, 16, 16);
            format.ConfigureBuffer(buffer);
            var (ox, oy, oz) = sample.Origin;
            generator.GenerateBlock(buffer, new Vector3(ox, oy, oz), 0);
            var sdf = buffer.GetChannelAsByteArray(TerrainChannels.Sdf);
            var materials = buffer.GetChannelAsByteArray(TerrainChannels.Material);
            for (var i = 0; i < materials.Length; i++)
            {
                if (BitConverter.ToInt16(sdf, i * 2) < 0)
                {
                    seen.Add(materials[i]);
                }
            }
        }

        buffer.Dispose();
        foreach (var (_, textureIndex) in TerrainMaterials.All)
        {
            AssertThat(seen.Contains(textureIndex)).IsTrue();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorV1_CavesExist()
    {
        var planet = Planet();
        using var probe = new GeneratorProbe(GeneratorProfiles.CreateGenerator(planet));
        var random = new Random(12345);
        var coreRadius = planet.RadiusMeters - planet.CoreDepthMeters;
        var low = coreRadius + PlanetGeneratorV1.CaveCoreMargin + 8f;
        var high = planet.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - PlanetGeneratorV1.CaveMinDepth - 2f;
        var air = 0;

        for (var i = 0; i < 4000; i++)
        {
            var dx = (random.NextDouble() * 2) - 1;
            var dy = (random.NextDouble() * 2) - 1;
            var dz = (random.NextDouble() * 2) - 1;
            var length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (length < 1e-6)
            {
                continue;
            }

            var radius = low + (random.NextDouble() * (high - low));
            var scale = radius / length;
            if (probe.Sample((int)Math.Floor(dx * scale), (int)Math.Floor(dy * scale), (int)Math.Floor(dz * scale)).Sdf > 0f)
            {
                air++;
            }
        }

        AssertThat(air).IsGreater(20);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Generator_UnknownProfile_Throws()
    {
        var planet = Planet() with { GeneratorProfile = "core:planet/999" };

        AssertThrown(() => GeneratorProfiles.CreateGenerator(planet)).IsInstanceOf<NotSupportedException>();
        AssertThat(GeneratorProfiles.IsSupported("core:planet/999")).IsFalse();
        AssertThat(GeneratorProfiles.IsSupported(GeneratorProfiles.Current)).IsTrue();
        AssertThat(GeneratorProfiles.BuildHashComponent).IsEqual("core:planet/1");
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Generator_VoxelSizeNot1_Throws()
    {
        var planet = Planet() with { VoxelSizeMeters = 0.5f };

        AssertThrown(() => GeneratorProfiles.CreateGenerator(planet)).IsInstanceOf<NotSupportedException>();
    }

    [TestCase]
    public void NoiseSeeds_GoldenValues()
    {
        // Part of the frozen generator output: a change here changes every planet.
        AssertThat(NoiseSeeds.For(0UL, 1)).IsEqual(NoiseSeeds.For(0UL, 1));
        AssertThat(NoiseSeeds.For(GalaxySeed, NoiseSeeds.OreSlot)).IsEqual(GoldenOreSeed);
    }

    private const int GoldenOreSeed = -938375772;
}
