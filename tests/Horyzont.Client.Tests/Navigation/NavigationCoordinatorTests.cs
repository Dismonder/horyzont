using System.Numerics;

using Horyzont.Client.Frames;
using Horyzont.Client.Markers;
using Horyzont.Client.Navigation;
using Horyzont.Client.Replication;
using Horyzont.Client.Space;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Space;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Markers;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Navigation;

public sealed class NavigationCoordinatorTests
{
    private const double RenderTick = 33;
    private static readonly EntityId _ship = new(5);

    /// <summary>The own position as the game stores it: a frame and a single-precision local position.</summary>
    private sealed class Viewpoint(ClientFrameGraph graph) : INavigationViewpoint
    {
        public FrameRef Frame { get; set; }

        public Vector3 Local { get; set; }

        public bool TryGetWorldPosition(double renderTick, out Vector3D position)
        {
            if (graph.TryGetWorldPose(Frame, renderTick, out var pose))
            {
                position = FrameMath.TransformPoint(pose, Vector3D.FromFloat(Local));
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>Places the viewpoint at a world position, expressed in <paramref name="frame"/> and rounded to single precision.</summary>
        public void PlaceAt(FrameRef frame, Vector3D world)
        {
            Assert.True(graph.TryGetWorldPose(frame, RenderTick, out var pose));
            var local = FrameMath.InverseTransformPoint(pose, world);
            Frame = frame;
            Local = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
        }
    }

    private sealed class Rig
    {
        public Rig(FrameRef? shipParent, Func<GeneratedStarSystem, Vector3>? shipPosition = null)
        {
            Created = StarSystemGenesis.CreateWorldBodies(11, "core:test/1");
            Home = Created.Bodies[0].Id;
            Moon = Created.Bodies[1].Id;
            var hello = new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), Created.Bodies, [], []);
            var time = new FakeTimeProvider();
            Entities = new EntityMirror(time);
            var constructs = new ConstructMirror(time);
            Stars = new ClientStarSystem();
            Frames = new ClientFrames(Entities, constructs, Stars);
            Stars.OnServerHello(hello);
            Frames.OnServerHello(hello);
            Stars.OnMessage(new StarSystemState(Created.System));
            Graph = Frames.Graph!;
            Poses = new ClientFramePoses(Graph);
            Markers = new ClientMarkerStore();
            Viewpoint = new Viewpoint(Graph);
            Coordinator = new NavigationCoordinator(Poses, Markers, Entities, Viewpoint);
            ShipFrame = FrameRef.Of(_ship);

            if (shipParent is { } parent)
            {
                var origin = shipPosition?.Invoke(Created) ?? new Vector3(0f, (float)Created.Bodies[0].RadiusMeters + 10f, 0f);
                Entities.Apply(new EntitySpawn(_ship, EntityKind.Construct, 0, null, new SnapshotFrame(parent, 0), State(origin), []));
                Assert.True(constructs.Apply(new ConstructState(
                    new ConstructHeaderRecord(_ship, parent, 0, ConstructMode.Ship, origin, Quaternion.Identity, 1), 0, 1, [])));
                var full = new Snapshot(33, new Tick(33), 0, 0, [new SnapshotFrame(parent, 0)], [State(origin)]);
                Entities.OnFullSnapshot(full);
                Frames.OnFullSnapshot(full);
            }
        }

        public GeneratedStarSystem Created { get; }

        public PlanetId Home { get; }

        public PlanetId Moon { get; }

        public EntityMirror Entities { get; }

        public ClientStarSystem Stars { get; }

        public ClientFrames Frames { get; }

        public ClientFrameGraph Graph { get; }

        public ClientFramePoses Poses { get; }

        public ClientMarkerStore Markers { get; }

        public Viewpoint Viewpoint { get; }

        public NavigationCoordinator Coordinator { get; }

        public FrameRef ShipFrame { get; }

        public Vector3D WorldOf(PlanetId planet, double x, double y, double z)
        {
            Assert.True(Graph.TryGetWorldPose(FrameRef.Of(planet), RenderTick, out var pose));
            return FrameMath.TransformPoint(pose, new Vector3D(x, y, z));
        }

        public void AddMarker(ulong id, PlanetId planet, Vector3 position, string name = "Target") =>
            Markers.OnMessage(new MarkersState(
                [.. Markers.All, new MarkerRecord(new MarkerId(id), null, planet, position, name, MarkerIcon.Poi, MarkerColor.Red)]));

        private static EntityState State(Vector3 position) => new(_ship, (EntityStateFields)15, 0, 0, position, Quaternion.Identity, Vector3.Zero, 0);
    }

    private static void AssertSameGuidance(Rig rig, Vector3D world, Vector3D target, params FrameRef[] frames)
    {
        var expected = target - world;
        NavigationGuidance? first = null;
        foreach (var frame in frames)
        {
            rig.Viewpoint.PlaceAt(frame, world);
            Assert.True(rig.Coordinator.TryGetGuidance(RenderTick, out var guidance), $"no guidance in frame {frame}");
            Assert.True(Vector3D.Distance(guidance.WorldDelta, expected) < 0.001, $"delta off by {Vector3D.Distance(guidance.WorldDelta, expected)} m in frame {frame}");
            Assert.True(Math.Abs(guidance.DistanceMeters - expected.Length()) < 0.001);
            if (first is { } previous)
            {
                Assert.True(Vector3D.Distance(previous.WorldDelta, guidance.WorldDelta) < 0.002, "the guidance jumped on a reparent");
            }

            first = guidance;
        }
    }

    [Fact]
    public void NavigationCoordinator_TargetOnMoon_FromPlanetShipAndSystemFrames_Continuous()
    {
        // Ship parked above the home planet, parented to the planet frame.
        var onPlanet = new Rig(FrameRef.Of(StarSystemGenesis.CreateWorldBodies(11, "core:test/1").Bodies[0].Id));
        var markerPosition = new Vector3(0f, (float)onPlanet.Created.Bodies[1].RadiusMeters + 2f, 0f);
        onPlanet.AddMarker(1, onPlanet.Moon, markerPosition);
        onPlanet.Coordinator.Select(NavigationTarget.ForMarker(new MarkerId(1)));
        var target = onPlanet.WorldOf(onPlanet.Moon, markerPosition.X, markerPosition.Y, markerPosition.Z);

        var nearHome = onPlanet.WorldOf(onPlanet.Home, 30, onPlanet.Created.Bodies[0].RadiusMeters + 14, 40);
        AssertSameGuidance(onPlanet, nearHome, target, FrameRef.Of(onPlanet.Home), onPlanet.ShipFrame, FrameRef.Of(onPlanet.Home));

        // The same ship in the system frame (K5-21 zone transition): the player stands on it at the same world position.
        var systemFrame = FrameRef.Of(onPlanet.Created.System.System);
        var inSystem = new Rig(systemFrame, created => new Vector3(1200f, 800f, -300f));
        inSystem.AddMarker(1, inSystem.Moon, markerPosition);
        inSystem.Coordinator.Select(NavigationTarget.ForMarker(new MarkerId(1)));
        var inSystemTarget = inSystem.WorldOf(inSystem.Moon, markerPosition.X, markerPosition.Y, markerPosition.Z);
        var shipWorld = new Vector3D(1200, 800, -300);
        AssertSameGuidance(inSystem, shipWorld + new Vector3D(2, 1, 3), inSystemTarget, inSystem.ShipFrame, inSystem.ShipFrame);

        // Near the moon: the planet frame of the moon and a ship parked in it describe the same point.
        var moonShip = new Rig(FrameRef.Of(onPlanet.Moon), created => new Vector3(0f, (float)created.Bodies[1].RadiusMeters + 12f, 0f));
        moonShip.AddMarker(1, moonShip.Moon, markerPosition);
        moonShip.Coordinator.Select(NavigationTarget.ForMarker(new MarkerId(1)));
        var moonTarget = moonShip.WorldOf(moonShip.Moon, markerPosition.X, markerPosition.Y, markerPosition.Z);
        var nearMoon = moonShip.WorldOf(moonShip.Moon, 5, moonShip.Created.Bodies[1].RadiusMeters + 14, 6);
        AssertSameGuidance(moonShip, nearMoon, moonTarget, FrameRef.Of(moonShip.Moon), moonShip.ShipFrame, FrameRef.Of(moonShip.Moon));
    }

    [Fact]
    public void NavigationCoordinator_TargetRemoved_Clears()
    {
        var rig = new Rig(null);
        rig.AddMarker(1, rig.Home, new Vector3(0f, 4000f, 0f));
        rig.Viewpoint.PlaceAt(FrameRef.Of(rig.Home), rig.WorldOf(rig.Home, 0, 4005, 0));
        var changes = 0;
        rig.Coordinator.SelectionChanged += () => changes++;
        rig.Coordinator.Select(NavigationTarget.ForMarker(new MarkerId(1)));

        Assert.True(rig.Coordinator.TryGetGuidance(RenderTick, out var guidance));
        Assert.True(Math.Abs(guidance.DistanceMeters - 5) < 0.001);
        Assert.Equal(NavigationTarget.ForMarker(new MarkerId(1)), guidance.Target);

        rig.Markers.OnMessage(new OperationApplied(new OpId(9), new Tick(1), new Core.Operations.MarkerRemoveOp(new MarkerId(1)), []));

        Assert.False(rig.Coordinator.TryGetGuidance(RenderTick, out _));
        Assert.Null(rig.Coordinator.Target);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void NavigationCoordinator_BodyAndMissingEntityTargets()
    {
        var rig = new Rig(null);
        rig.Viewpoint.PlaceAt(FrameRef.Of(rig.Home), rig.WorldOf(rig.Home, 0, 4010, 0));
        rig.Coordinator.Select(NavigationTarget.ForBody(rig.Home));

        Assert.True(rig.Coordinator.TryGetGuidance(RenderTick, out var toHome));
        Assert.True(Math.Abs(toHome.DistanceMeters - 4010) < 0.001);

        rig.Coordinator.Select(NavigationTarget.ForEntity(new EntityId(77)));
        Assert.False(rig.Coordinator.TryGetGuidance(RenderTick, out _));
        Assert.Null(rig.Coordinator.Target);
    }

    [Fact]
    public void NavigationCoordinator_NoViewpoint_NoGuidanceButKeepsTarget()
    {
        var rig = new Rig(null);
        rig.AddMarker(1, rig.Home, new Vector3(0f, 4000f, 0f));
        rig.Coordinator.Select(NavigationTarget.ForMarker(new MarkerId(1)));
        rig.Viewpoint.Frame = FrameRef.Of(new PlanetId(999));

        Assert.False(rig.Coordinator.TryGetGuidance(RenderTick, out _));
        Assert.NotNull(rig.Coordinator.Target);
    }

    [Fact]
    public void NavigationCoordinator_Update_AllocatesZeroBytes()
    {
        var rig = new Rig(FrameRef.Of(StarSystemGenesis.CreateWorldBodies(11, "core:test/1").Bodies[0].Id));
        rig.AddMarker(1, rig.Moon, new Vector3(0f, 1500f, 0f));
        rig.Coordinator.Select(NavigationTarget.ForMarker(new MarkerId(1)));
        rig.Viewpoint.PlaceAt(rig.ShipFrame, rig.WorldOf(rig.Home, 1, 4015, 1));
        Assert.True(rig.Coordinator.TryGetGuidance(RenderTick, out _));

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var ok = true;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10_000; i++)
            {
                ok &= rig.Coordinator.TryGetGuidance(RenderTick, out _);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(ok);
        }

        var allocated = smallest;

        Assert.Equal(0, allocated);
    }
}
