using System.Diagnostics.CodeAnalysis;

using GdUnit4;

using Godot;

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
        planets.Initialize(() => provider, () => system, () => Viewpoint(forced, home, camera, camera), render: false, () => false);

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

    /// <summary>The viewers move with the active body: the view (and so <c>TerrainView</c>) is the one of the active planet.</summary>
    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task ClientPlanetTerrains_Viewers_FollowActiveBody()
    {
        await using var scene = EngineTestScene.Create();
        var created = StarSystemGenesis.CreateWorldBodies(21, GeneratorProfiles.Current);
        var system = new ClientStarSystem();
        system.OnServerHello(new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), created.Bodies, [], []));
        system.Apply(created.System);
        var home = system.Model!.GetBody(0);
        var moon = system.Model.GetBody(1);
        var provider = new RecordingProvider(scene.Add(new Node3D { Name = "Anchors" }));
        var feet = home.Position + new Vector3D(0, home.RadiusMeters, 0);
        var camera = feet + new Vector3D(0, 3, -4);
        var forced = home.Planet;
        var planets = scene.Add(new ClientPlanetTerrains());
        planets.Initialize(() => provider, () => system, () => Viewpoint(forced, home, camera, feet), render: true, () => true);

        planets.Step(0);
        AssertThat(planets.VisualViewer).IsNotNull();
        AssertThat(planets.CollisionViewer).IsNotNull();
        AssertThat(planets.VisualViewer!.GetParent() == provider.Anchor(home.Planet)).IsTrue();
        AssertThat(planets.VisualViewer.Position.DistanceTo(new Vector3(0, (float)home.RadiusMeters + 3, -4)) < 1e-2).IsTrue();
        AssertThat(planets.VisualViewer.ViewDistance).IsEqual(2048u);
        AssertThat(planets.TryGetViewers(out var viewers)).IsTrue();
        AssertThat(viewers.Planet).IsEqual(home.Planet);
        AssertThat(viewers.CollisionDistance).IsEqual(64u);
        AssertThat(viewers.VisualDistance).IsEqual(2048u);

        // In a ship between the bodies near the moon: the camera viewer moves to the moon, the view is the one of the moon, the
        // collision position is the feet in the moon frame (no collision viewer there).
        forced = default;
        feet = moon.Position + new Vector3D(0, moon.RadiusMeters * 3.9, 0);
        camera = feet + new Vector3D(0, 3, -4);
        planets.Step(1);
        AssertThat(planets.VisualViewer).IsNotNull();
        AssertThat(planets.VisualViewer!.GetParent() == provider.Anchor(moon.Planet)).IsTrue();
        AssertThat(planets.VisualViewer.ViewDistance).IsEqual(16384u);
        AssertThat(planets.TryGetViewers(out viewers)).IsTrue();
        AssertThat(viewers.Planet).IsEqual(moon.Planet);
        AssertThat(viewers.CollisionDistance).IsEqual(0u);
        AssertThat(System.Numerics.Vector3.Distance(viewers.CollisionPosition, new System.Numerics.Vector3(0, (float)(moon.RadiusMeters * 3.9), 0)) < 0.05f).IsTrue();

        // The home terrain is released after the fade; its viewers went with it.
        planets.Step(1 + ClientTerrainSelection.FadeSeconds + 1);
        AssertThat(provider.Active.Contains(home.Planet.Value)).IsFalse();
        AssertThat(provider.Anchor(home.Planet).GetChildCount()).IsEqual(0);
    }

    /// <summary>The own body on <paramref name="body"/>, with the camera and the feet given in the system frame.</summary>
    private static ClientViewpoint Viewpoint(PlanetId forced, BodyInfo body, Vector3D camera, Vector3D feet) =>
        new(forced, body.Planet, (float)body.RadiusMeters, Local(camera - body.Position), Local(feet - body.Position));

    private static System.Numerics.Vector3 Local(Vector3D value) => new((float)value.X, (float)value.Y, (float)value.Z);

    private sealed class RecordingProvider(Node3D? root = null) : IClientTerrainProvider
    {
        private readonly Dictionary<ulong, View> _views = [];

        public SortedSet<ulong> Active { get; } = [];

        public Node3D Anchor(PlanetId planet) => _views[planet.Value].Anchor;

        public void Prepare(ServerHello hello)
        {
        }

        public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out IClientTerrainView? view)
        {
            view = root is not null && Active.Contains(planet.Value) ? _views[planet.Value] : null;
            return view is not null;
        }

        public bool Activate(PlanetId planet)
        {
            Active.Add(planet.Value);
            if (root is not null && !_views.ContainsKey(planet.Value))
            {
                var anchor = new Node3D { Name = $"Planet{planet.Value}" };
                root.AddChild(anchor);
                _views.Add(planet.Value, new View(planet, anchor));
            }

            return true;
        }

        public void Release(PlanetId planet) => Active.Remove(planet.Value);
    }

    private sealed class View(PlanetId planet, Node3D anchor) : IClientTerrainView
    {
        public PlanetId Planet => planet;

        public Node3D Anchor => anchor;

        public uint CollisionLayer => 1;

        public void AddViewer(VoxelViewer viewer) => anchor.AddChild(viewer);

        public void RemoveViewer(VoxelViewer viewer)
        {
            if (viewer.GetParent() == anchor)
            {
                anchor.RemoveChild(viewer);
            }
        }
    }
}
