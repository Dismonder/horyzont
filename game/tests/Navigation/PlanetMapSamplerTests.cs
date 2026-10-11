using GdUnit4;

using Horyzont.Client.Navigation;
using Horyzont.Core.World;
using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.UI.Map;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Navigation;

/// <summary>Engine tests of the planet map sampler (K5-27). Run one at a time on the runner (never in parallel).</summary>
[TestSuite]
public class PlanetMapSamplerTests
{
    private const ulong GalaxySeed = 0x484F52595A4F4E54UL;

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task PlanetMapSampler_HeightsMatchGeneratorWithinVoxel()
    {
        await using var scene = EngineTestScene.Create();
        var planet = PlanetGenesis.CreatePlanet(GalaxySeed, PlanetGenesis.HomePlanetIndex, GeneratorProfiles.Current);
        var cache = new PlanetMapCache();
        var map = cache.Get(planet);
        var ready = await scene.WaitFramesUntil(map.Poll, 3000);
        AssertThat(ready).IsTrue();
        AssertThat(map.Texture is not null).IsTrue();
        AssertThat(map.Heights.Count == PlanetMapProjection.Width * PlanetMapProjection.Height).IsTrue();

        // The second request is answered from the cache with the same heights.
        AssertThat(ReferenceEquals(cache.Get(planet), map)).IsTrue();

        using var probe = new GeneratorProbe(GeneratorProfiles.CreateGenerator(planet));
        var random = new Random(7);
        var checkedColumns = 0;
        for (var i = 0; i < 32; i++)
        {
            var column = random.Next(PlanetMapProjection.Width);
            var row = random.Next(8, PlanetMapProjection.Height - 8);
            var direction = PlanetMapProjection.Direction((column + 0.5f) / PlanetMapProjection.Width, (row + 0.5f) / PlanetMapProjection.Height);

            // Dense reference: the outermost solid voxel along the ray, scanned inward in half-metre steps.
            var surface = float.NaN;
            for (var radius = planet.RadiusMeters + SurfaceBisection.DefaultRelief; radius >= planet.RadiusMeters - SurfaceBisection.DefaultRelief; radius -= 0.5f)
            {
                var p = direction * radius;
                if (probe.Sample((int)MathF.Round(p.X), (int)MathF.Round(p.Y), (int)MathF.Round(p.Z)).Sdf <= 0f)
                {
                    surface = radius;
                    break;
                }
            }

            if (float.IsNaN(surface))
            {
                continue;
            }

            checkedColumns++;
            var mapped = map.Heights[(row * PlanetMapProjection.Width) + column] + planet.RadiusMeters;
            AssertThat(MathF.Abs(mapped - surface) <= 1.0f).IsTrue();
        }

        AssertThat(checkedColumns >= 24).IsTrue();
    }
}
