using GdUnit4;

using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Frames;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Frames;

/// <summary>K5-20 step 13 (decision 18): frame spaces share the world physics space; gravity and walking per frame kind.</summary>
[TestSuite]
public class FrameSpaceTests
{
    private static readonly PlanetParams _planet = new(new PlanetId(1), new SystemId(1), 7, "default");
    private static readonly FrameRef _ship = FrameRef.Of(new EntityId(5));

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task FrameSpace_SharedWorld_PlanetConstructAndSystem()
    {
        await using var scene = EngineTestScene.Create();
        var source = new Source();
        var nodes = new FrameNodes { Name = "Frames" };
        nodes.Initialize(source);
        scene.Add(nodes);
        var graph = new FrameGraph(source);
        var mode = ConstructMode.Ship;
        var spaces = new SharedWorldFrameSpaces(nodes, () => graph, id => id == _planet.Id ? _planet : null, id => id.Value == 5 ? mode : null);

        // Planet: the physics space of the world, radial gravity, walking, transform of the planet node (115 km from the origin).
        AssertThat(spaces.TryGet(FrameRef.Of(_planet.Id), out var planet)).IsTrue();
        AssertThat(planet.PhysicsSpace).IsEqual(nodes.GetWorld3D().Space);
        AssertThat(planet.SupportsWalking).IsTrue();
        AssertThat(Math.Abs(planet.GlobalTransform.Origin.X - 115_000)).IsLess(1e-9);
        var g = planet.GravityAt(new Vector3D(0, 4000, 0));
        AssertThat(Vector3D.Distance(g, new Vector3D(0, -_planet.Gravity, 0))).IsLess(1e-5);

        // Construct: the same physics space, region gravity in the construct frame, walking only when static.
        AssertThat(spaces.TryGet(_ship, out var ship)).IsTrue();
        AssertThat(ship.PhysicsSpace).IsEqual(planet.PhysicsSpace);
        AssertThat(ship.SupportsWalking).IsFalse();
        // K5-21 decision 17: a construct has the zone gravity of the body, g0 (R/r)^2 at 4100 m.
        var inShip = ship.GravityAt(Vector3D.Zero);
        AssertThat(Math.Abs(inShip.Length() - StarSystemMath.Gravity(4100, _planet.RadiusMeters, _planet.Gravity))).IsLess(1e-3);
        AssertThat(Vector3D.Distance(inShip, new Vector3D(0, -_planet.Gravity, 0))).IsGreater(1);
        mode = ConstructMode.Static;
        AssertThat(spaces.TryGet(_ship, out var parked)).IsTrue();
        AssertThat(parked.SupportsWalking).IsTrue();

        // System: no gravity, no walking; unknown frames have no space.
        AssertThat(spaces.TryGet(FrameRef.Of(new SystemId(1)), out var system)).IsTrue();
        AssertThat(system.GravityAt(new Vector3D(0, 4000, 0))).IsEqual(Vector3D.Zero);
        AssertThat(spaces.TryGet(FrameRef.Of(new EntityId(9)), out _)).IsFalse();

        // Planet spaces convert through the planet node: a planet-frame point and back.
        var local = new System.Numerics.Vector3(1, 4000, -2);
        var global = spaces.Planet(_planet.Id).ToGodot(local);
        AssertThat(Math.Abs(global.X - 115_001)).IsLess(1e-6);
        AssertThat((double)System.Numerics.Vector3.Distance(spaces.Planet(_planet.Id).ToPlanetLocal(global), local)).IsLess(1e-4);
        await scene.WaitFrames(1);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task FrameSpace_WithoutNodes_PlanetInIdentity()
    {
        await using var scene = EngineTestScene.Create();
        var space = ((FrameNodes?)null).PlanetSpaceOf(_planet.Id, _planet);
        AssertThat(space.GlobalTransform).IsEqual(Transform3D.Identity);
        var local = new System.Numerics.Vector3(3, 4000, 5);
        AssertThat(space.ToGodot(local)).IsEqual(new Vector3(3, 4000, 5));
        await scene.WaitFrames(1);
    }

    /// <summary>A system with the planet 115 km from the origin and a ship tilted by 90° above it.</summary>
    private sealed class Source : IFramePoseSource
    {
        public bool TryGetParent(FrameRef frame, out FrameRef parent) => TryGetLocalPose(frame, out parent, out _);

        public bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent)
        {
            parent = default;
            poseInParent = PoseD.Identity;
            if (frame == FrameRef.Of(new SystemId(1)))
            {
                return true;
            }

            if (frame == FrameRef.Of(_planet.Id))
            {
                parent = FrameRef.Of(new SystemId(1));
                poseInParent = new PoseD(new Vector3D(115_000, 0, 0), QuaternionD.Identity);
                return true;
            }

            if (frame == _ship)
            {
                parent = FrameRef.Of(_planet.Id);
                poseInParent = new PoseD(new Vector3D(0, 4100, 0), QuaternionD.FromAxisAngle(new Vector3D(0, 0, 1), Math.PI / 2));
                return true;
            }

            return false;
        }
    }
}
