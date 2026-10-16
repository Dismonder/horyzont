using GdUnit4;

using Godot;

using Horyzont.Client.Props;
using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Game.Client.Terrain;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Props;
using Horyzont.Game.Planet.Rendering;
using Horyzont.Game.Planet.Surface;
using Horyzont.Game.Tests.Support;
using Horyzont.Server.Props;

using Microsoft.Extensions.Logging.Abstractions;

using static GdUnit4.Assertions;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Planet;

/// <summary>
/// K6-31 D4 (no window): the structure and the counts of the drawing of the generator props. How the props look is for the owner to judge
/// (screenshots); here the meshes are boxes and only the nodes, the instance counts and the rebuilds are checked, against the very same
/// anchors the server sees.
/// </summary>
[TestSuite]
public class PropRendererTests
{
    private const ulong Seed = 21;

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Regions_InRadius_HaveInstancesOfLayoutMinusHarvested()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        var camera = rig.CameraAt(0);
        rig.Move(camera);
        AssertThat(await rig.WaitAllBuilt(camera)).IsTrue();

        var regions = rig.WantedRegions(camera);
        AssertThat(regions.Count > 50).IsTrue();
        var instances = 0;
        foreach (var region in regions)
        {
            var anchors = PropRegionAnchors.Compute(rig.Home, rig.Layout, rig.Sampler, region);
            AssertThat(rig.Renderer.InstanceCount(rig.Home.Id, region)).IsEqual(anchors.Length);
            instances += anchors.Length;
        }

        AssertThat(instances > 1000).IsTrue();

        // One region loses its first prop: the same region draws one instance less, nothing else is rebuilt.
        var target = regions.First(r => PropRegionAnchors.Compute(rig.Home, rig.Layout, rig.Sampler, r).Length > 3);
        var all = PropRegionAnchors.Compute(rig.Home, rig.Layout, rig.Sampler, target);
        var builds = regions.ToDictionary(r => r.Value, r => rig.Renderer.Builds(rig.Home.Id, r));
        rig.States.Harvest(rig.Home.Id, all[0].Instance.Id);
        AssertThat(await scene.WaitFramesUntil(() => rig.Renderer.Builds(rig.Home.Id, target) == builds[target.Value] + 1, 120)).IsTrue();
        AssertThat(rig.Renderer.InstanceCount(rig.Home.Id, target)).IsEqual(all.Length - 1);
        foreach (var region in regions.Where(r => r != target))
        {
            AssertThat(rig.Renderer.Builds(rig.Home.Id, region)).IsEqual(builds[region.Value]);
        }

        // It grows back: the instance returns and again only this region is rebuilt.
        rig.States.Regrow(rig.Home.Id, all[0].Instance.Id);
        AssertThat(await scene.WaitFramesUntil(() => rig.Renderer.Builds(rig.Home.Id, target) == builds[target.Value] + 2, 120)).IsTrue();
        AssertThat(rig.Renderer.InstanceCount(rig.Home.Id, target)).IsEqual(all.Length);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Region_Harvested_SkippedAtFirstBuild()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        var camera = rig.CameraAt(0);
        var region = rig.WantedRegions(camera).First(r => PropRegionAnchors.Compute(rig.Home, rig.Layout, rig.Sampler, r).Length > 3);
        var all = PropRegionAnchors.Compute(rig.Home, rig.Layout, rig.Sampler, region);
        rig.States.Harvest(rig.Home.Id, all[1].Instance.Id);
        rig.States.Harvest(rig.Home.Id, all[2].Instance.Id);
        rig.Move(camera);
        AssertThat(await rig.WaitAllBuilt(camera)).IsTrue();
        AssertThat(rig.Renderer.InstanceCount(rig.Home.Id, region)).IsEqual(all.Length - 2);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Regions_FarFromCamera_AreFreedAndNewOnesBuilt()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        var first = rig.CameraAt(0);
        rig.Move(first);
        AssertThat(await rig.WaitAllBuilt(first)).IsTrue();
        var before = rig.WantedRegions(first);

        var second = rig.CameraAt(1500);
        rig.Move(second);
        AssertThat(await rig.WaitAllBuilt(second)).IsTrue();
        foreach (var region in before)
        {
            AssertThat(rig.Renderer.RegionNode(rig.Home.Id, region)).IsNull();
        }

        AssertThat(rig.Renderer.BuiltRegions).IsEqual(rig.WantedRegions(second).Count);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Source_ListsTheAnchorsOfTheBuiltRegions()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene);
        var camera = rig.CameraAt(0);
        rig.Move(camera);
        AssertThat(await rig.WaitAllBuilt(camera)).IsTrue();

        var found = new List<PropInstance>();
        rig.Renderer.CollectInSphere(rig.Home.Id, camera, 40f, found);
        AssertThat(found.Count > 0).IsTrue();
        foreach (var prop in found)
        {
            AssertThat(NumVector3.Distance(prop.Anchor, camera) <= 40f).IsTrue();
        }

        // Exactly the props of the layout inside the sphere: the cells around it, computed independently.
        var expected = 0;
        foreach (var region in rig.WantedRegions(camera))
        {
            expected += PropRegionAnchors.Compute(rig.Home, rig.Layout, rig.Sampler, region).Count(v => NumVector3.Distance(v.Instance.Anchor, camera) <= 40f);
        }

        AssertThat(found.Count).IsEqual(expected);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Anchors_EqualThoseOfThePropFieldOfTheServer()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = GeneratedWorld.Create(scene, Seed);
        var field = new PropField(world.State, world.Terrain.Service);
        var home = world.Home;
        AssertThat(PropLayouts.TryGet(home.GeneratorProfile, out var layout)).IsTrue();
        var sampler = new PlanetSurfaceSampler(home, GeneratorProfiles.CreateGenerator(home));
        AssertThat(CubeSphereGrid.TryGetCellsPerFace(home.RadiusMeters, out var cells)).IsTrue();
        var checkedProps = 0;
        foreach (var (face, u, v) in new[] { (0, 5, 7), (3, 40, 12), (5, cells / 8 / 2, cells / 8 / 2) })
        {
            var region = PropRegionAnchors.KeyOf(face, u, v);
            var visuals = PropRegionAnchors.Compute(home, layout!, sampler, region);
            var seen = new HashSet<ulong>();
            foreach (var visual in visuals)
            {
                AssertThat(field.TryGetProp(home.Id, visual.Instance.Id, out var prop)).IsTrue();
                AssertThat(prop.Kind).IsEqual(visual.Instance.Kind);
                AssertThat(prop.Anchor).IsEqual(visual.Instance.Anchor);
                AssertThat(prop.Scale).IsEqual(visual.Instance.Scale);
                seen.Add(visual.Instance.Id.Value);
                checkedProps++;
            }

            // And nothing the server has is missing from the client.
            for (var cu = 0; cu < 8; cu++)
            {
                for (var cv = 0; cv < 8; cv++)
                {
                    for (var slot = 0; slot < 4; slot++)
                    {
                        var id = PropId.Create(face, (u * 8) + cu, (v * 8) + cv, slot);
                        AssertThat(field.TryGetProp(home.Id, id, out _)).IsEqual(seen.Contains(id.Value));
                    }
                }
            }
        }

        AssertThat(checkedProps > 100).IsTrue();
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Renderer_RealFloraMeshes_LoadAndDraw()
    {
        await using var scene = EngineTestScene.Create();
        var rig = Rig.Create(scene, realMeshes: true);
        var camera = rig.CameraAt(0);
        rig.Move(camera);
        AssertThat(await rig.WaitAllBuilt(camera)).IsTrue();
        var region = rig.WantedRegions(camera).First(r => rig.Renderer.InstanceCount(rig.Home.Id, r) > 3);
        var node = rig.Renderer.RegionNode(rig.Home.Id, region)!;
        AssertThat(node.GetChildCount() > 0).IsTrue();
        foreach (var child in node.GetChildren().OfType<MultiMeshInstance3D>())
        {
            AssertThat(child.Multimesh.Mesh).IsNotNull();
            AssertThat(child.Multimesh.VisibleInstanceCount).IsEqual(child.Multimesh.InstanceCount);
        }
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task PlanetFlora_KeepsNoGameplayKinds()
    {
        await using var scene = EngineTestScene.Create();
        var home = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current).Bodies[0];
        var instancer = PlanetFlora.Create(home);
        scene.Add(instancer);
        foreach (var gameplay in new[] { "Pine", "PineTall", "Broadleaf", "BroadleafAutumn", "Bush", "Rock", "Boulder", "Crystals" })
        {
            AssertThat(instancer.Library.Call("find_item_by_name", gameplay).AsInt32()).IsEqual(-1);
        }

        foreach (var cover in new[] { "Grass", "GrassFlowersYellow", "GrassFlowersPink", "GrassFlowersBlue" })
        {
            AssertThat(instancer.Library.Call("find_item_by_name", cover).AsInt32() >= 0).IsTrue();
        }

        await scene.WaitFrames(1);
    }

    private sealed class Rig
    {
        private Rig(PlanetParams home, IPropLayout layout, PlanetSurfaceSampler sampler, TestStates states, PropRenderer renderer, int cells)
        {
            Home = home;
            Layout = layout;
            Sampler = sampler;
            States = states;
            Renderer = renderer;
            Cells = cells;
        }

        public PlanetParams Home { get; }

        public IPropLayout Layout { get; }

        public PlanetSurfaceSampler Sampler { get; }

        public TestStates States { get; }

        public PropRenderer Renderer { get; }

        public int Cells { get; }

        public NumVector3 Camera { get; private set; }

        public static Rig Create(EngineTestScene scene, bool realMeshes = false)
        {
            var home = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current).Bodies[0];
            PropLayouts.TryGet(home.GeneratorProfile, out var layout);
            CubeSphereGrid.TryGetCellsPerFace(home.RadiusMeters, out var cells);
            var sampler = new PlanetSurfaceSampler(home, GeneratorProfiles.CreateGenerator(home));
            var states = new TestStates();
            var renderer = new PropRenderer { Name = "PropRenderer" };
            Rig? rig = null;
            renderer.Initialize(
                () => new[] { home },
                static () => null,
                () => new ClientViewpoint(default, home.Id, home.RadiusMeters, rig!.Camera, rig.Camera),
                states,
                NullLogger.Instance,
                _ => sampler,
                realMeshes ? null : () => BoxMeshes());
            rig = new Rig(home, layout!, sampler, states, renderer, cells);
            rig.Camera = rig.CameraAt(0);
            scene.Add(renderer);
            return rig;
        }

        /// <summary>A point on the surface sphere a given distance along the face from the middle of face 0.</summary>
        public NumVector3 CameraAt(float metersAlong)
        {
            var middle = Cells / 2;
            var direction = CubeSphereGrid.DirectionOfPoint(0, middle, middle, Cells);
            var tangent = NumVector3.Normalize(NumVector3.Cross(direction, NumVector3.UnitY));
            var moved = NumVector3.Normalize(direction + (tangent * (metersAlong / Home.RadiusMeters)));
            return moved * Home.RadiusMeters;
        }

        public void Move(NumVector3 camera) => Camera = camera;

        public List<PropRegionKey> WantedRegions(NumVector3 camera)
        {
            var regions = new List<PropRegionKey>();
            new PropInterest().CollectRegions(Home, camera, PropRenderer.RenderRadiusMeters, regions);
            return regions;
        }

        public async Task<bool> WaitAllBuilt(NumVector3 camera)
        {
            var count = WantedRegions(camera).Count;
            var scene = (SceneTree)Engine.GetMainLoop();
            for (var i = 0; i < 1500; i++)
            {
                if (Renderer.BuiltRegions == count && Renderer.PendingJobs == 0)
                {
                    return true;
                }

                await scene.ToSignal(scene, SceneTree.SignalName.ProcessFrame);
            }

            return false;
        }

        private static Dictionary<string, Mesh> BoxMeshes()
        {
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            foreach (var kind in Enum.GetValues<PropKindV1>().Where(k => k != PropKindV1.None))
            {
                meshes[kind.ToString()] = new BoxMesh();
            }

            return meshes;
        }
    }

    private sealed class TestStates : IPropStateView
    {
        private readonly HashSet<(ulong Planet, ulong Prop)> _harvested = [];

        public event Action<PlanetId, PropRegionKey>? RegionChanged;

        public bool IsHarvested(PlanetId planet, PropId prop) => _harvested.Contains((planet.Value, prop.Value));

        public void Harvest(PlanetId planet, PropId prop)
        {
            _harvested.Add((planet.Value, prop.Value));
            RegionChanged?.Invoke(planet, prop.Region);
        }

        public void Regrow(PlanetId planet, PropId prop)
        {
            _harvested.Remove((planet.Value, prop.Value));
            RegionChanged?.Invoke(planet, prop.Region);
        }
    }
}
