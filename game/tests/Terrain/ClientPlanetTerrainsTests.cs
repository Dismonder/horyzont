using System.Diagnostics.CodeAnalysis;

using GdUnit4;

using Horyzont.Client.Space;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Space;
using Horyzont.Game.Client;
using Horyzont.Game.Client.Terrain;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>K5-22 decision 10: the client activates the terrain of the body it approaches and releases the others (engine).</summary>
[TestSuite]
public class ClientPlanetTerrainsTests
{
    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task ClientPlanetTerrains_ApproachMoon_ActivatesAt4R_ReleasesHome()
    {
        await using var scene = EngineTestScene.Create();
        var created = StarSystemGenesis.CreateWorldBodies(21, GeneratorProfiles.Current);
        var system = new ClientStarSystem();
        system.OnServerHello(new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), created.Bodies, [], []));
        system.Apply(created.System);
        var home = system.Model!.GetBody(0);
        var moon = system.Model.GetBody(1);
        var provider = new RecordingProvider();
        var camera = home.Position + new Vector3D(0, home.RadiusMeters + 10, 0);
        var forced = home.Planet;
        var planets = scene.Add(new ClientPlanetTerrains());
        planets.Initialize(() => provider, () => system, () => forced, () => (true, camera), render: false);

        planets.Step(0);
        AssertThat(provider.Active.Contains(home.Planet.Value)).IsTrue();
        AssertThat(provider.Active.Contains(moon.Planet.Value)).IsFalse();

        // The own body leaves the planet (in a ship between the bodies); the camera approaches the moon.
        forced = default;
        camera = moon.Position + new Vector3D(0, moon.RadiusMeters * 4.2, 0);
        planets.Step(1);
        AssertThat(provider.Active.Contains(moon.Planet.Value)).IsFalse();

        camera = moon.Position + new Vector3D(0, moon.RadiusMeters * 3.9, 0);
        planets.Step(2);
        AssertThat(provider.Active.Contains(moon.Planet.Value)).IsTrue();

        // Home fades for 10 s, then it is released.
        planets.Step(2 + ClientTerrainSelection.FadeSeconds + 1);
        AssertThat(provider.Active.Contains(home.Planet.Value)).IsFalse();
        AssertThat(provider.Active.Contains(moon.Planet.Value)).IsTrue();
    }

    private sealed class RecordingProvider : IClientTerrainProvider
    {
        public SortedSet<ulong> Active { get; } = [];

        public void Prepare(ServerHello hello)
        {
        }

        public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out IClientTerrainView? view)
        {
            view = null;
            return false;
        }

        public bool Activate(PlanetId planet)
        {
            Active.Add(planet.Value);
            return true;
        }

        public void Release(PlanetId planet) => Active.Remove(planet.Value);
    }
}
