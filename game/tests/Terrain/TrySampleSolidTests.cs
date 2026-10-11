using GdUnit4;

using Horyzont.Game.Tests.Support;
using Horyzont.Testing;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>K5-23: the density probe that decides whether a ship block is embedded in the ground.</summary>
[TestSuite]
public class TrySampleSolidTests
{
    private const float S = TerrainTestWorld.Surface;

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task TrySampleSolid_InsideAboveAndUnloaded()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        world.AddViewer(TerrainTestWorld.Ground(0, 0));
        AssertThat(await world.WaitLoaded(TerrainTestWorld.Ground(0, 0), 24)).IsTrue();

        var inside = world.Service.TrySampleSolid(WorldFactory.HomePlanet, new Vector3(1.3f, S - 2.5f, -2.7f), out var solidInside);
        var above = world.Service.TrySampleSolid(WorldFactory.HomePlanet, new Vector3(1.3f, S + 2.5f, -2.7f), out var solidAbove);
        var unloaded = world.Service.TrySampleSolid(WorldFactory.HomePlanet, new Vector3(5000f, S - 2.5f, 5000f), out var solidUnloaded);
        var unknownPlanet = world.Service.TrySampleSolid(new Horyzont.Core.Ids.PlanetId(999), new Vector3(1.3f, S - 2.5f, -2.7f), out _);
        var notFinite = world.Service.TrySampleSolid(WorldFactory.HomePlanet, new Vector3(float.NaN, S, 0f), out _);

        AssertThat(inside).IsTrue();
        AssertThat(solidInside).IsTrue();
        AssertThat(above).IsTrue();
        AssertThat(solidAbove).IsFalse();
        AssertThat(unloaded).IsFalse();
        AssertThat(solidUnloaded).IsFalse();
        AssertThat(unknownPlanet).IsFalse();
        AssertThat(notFinite).IsFalse();
    }
}
