using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Surface;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>K6-31 D6: <c>VoxelTerrainService.TrySampleGeneratedSurface</c> is the surface sampler of the planet, bit for bit (engine).</summary>
[TestSuite]
public class GeneratedSurfaceTests
{
    private const ulong Seed = 21;

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task TrySampleGeneratedSurface_ActiveAndInactivePlanet_EqualsSurfaceRadiusBitForBit()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = GeneratedWorld.Create(scene, Seed);
        var service = world.Terrain.Voxel;
        var inactive = world.Inactive;
        AssertThat(world.Terrain.Planets.IsActive(world.Home.Id)).IsTrue();
        AssertThat(world.Terrain.Planets.IsActive(inactive.Id)).IsFalse();

        foreach (var planet in new[] { world.Home, inactive })
        {
            var reference = new PlanetSurfaceSampler(planet, GeneratorProfiles.CreateGenerator(planet));
            var random = new Random(7);
            for (var i = 0; i < 100; i++)
            {
                var direction = new NumVector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
                AssertThat(service.TrySampleGeneratedSurface(planet.Id, direction, out var radius)).IsTrue();
                AssertThat(BitConverter.SingleToInt32Bits(radius)).IsEqual(BitConverter.SingleToInt32Bits(reference.SurfaceRadius(direction)));
                AssertThat(radius >= planet.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - 2f).IsTrue();
                AssertThat(radius <= planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + 2f).IsTrue();
            }
        }
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task TrySampleGeneratedSurface_UnknownPlanetOrBadDirection_False()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = GeneratedWorld.Create(scene, Seed);
        var service = world.Terrain.Voxel;

        AssertThat(service.TrySampleGeneratedSurface(new PlanetId(999), NumVector3.UnitY, out var radius)).IsFalse();
        AssertThat(radius).IsEqual(0f);
        AssertThat(service.TrySampleGeneratedSurface(world.Home.Id, NumVector3.Zero, out _)).IsFalse();
        AssertThat(service.TrySampleGeneratedSurface(world.Home.Id, new NumVector3(float.NaN, 1, 0), out _)).IsFalse();
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task TrySampleGeneratedSurface_OffMainThread_Throws()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = GeneratedWorld.Create(scene, Seed);
        var service = world.Terrain.Voxel;
        var home = world.Home.Id;

        var thrown = await Task.Run(() =>
        {
            try
            {
                service.TrySampleGeneratedSurface(home, NumVector3.UnitY, out _);
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        });

        AssertThat(thrown).IsTrue();
    }
}
