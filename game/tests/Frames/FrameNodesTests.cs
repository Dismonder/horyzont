using GdUnit4;

using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Game.Frames;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Frames;

/// <summary>K5-20 step 12: the frame tree keeps global transforms across reparents; planet conversions go through the planet node.</summary>
[TestSuite]
public class FrameNodesTests
{
    private static readonly SystemId _system = new(1);
    private static readonly PlanetId _planet = new(1);
    private static readonly FrameRef _ship = FrameRef.Of(new EntityId(5));

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task FrameNodes_Reparent_KeepsGlobalTransform()
    {
        await using var scene = EngineTestScene.Create();
        var source = new Source();
        var frames = new FrameNodes { Name = "Frames" };
        frames.Initialize(source);
        scene.Add(frames);

        var body = new Node3D { Name = "Body", Position = new Vector3(3, 4001, 7) };
        AssertThat(frames.Attach(body, FrameRef.Of(_planet))).IsTrue();
        var before = body.GlobalTransform;
        AssertThat(Math.Abs(before.Origin.X - 115_003)).IsLess(1e-9);

        frames.Reparent(body, _ship);
        AssertThat(frames.PendingReparents).IsEqual(1);
        frames.Compose();
        AssertThat(frames.PendingReparents).IsEqual(0);
        AssertThat(frames.TryGetNode(_ship, out var shipNode)).IsTrue();
        AssertThat(body.GetParent() == shipNode).IsTrue();
        AssertThat((double)body.GlobalTransform.Origin.DistanceTo(before.Origin)).IsLess(1e-9);

        // The ship moves in the compose step: the body moves with it.
        source.Ship = source.Ship with { Position = source.Ship.Position + new Vector3D(1, 0, 0) };
        frames.Compose();
        AssertThat(Math.Abs(body.GlobalTransform.Origin.X - before.Origin.X - 1)).IsLess(1e-9);
        await scene.WaitFrames(1);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task FrameNodes_PlanetOffset115km_PlanetFramesRoundTrip()
    {
        await using var scene = EngineTestScene.Create();
        var frames = new FrameNodes { Name = "Frames" };
        frames.Initialize(new Source());
        scene.Add(frames);

        var local = new System.Numerics.Vector3(12.5f, 4000.25f, -8f);
        var global = frames.ToGlobal(_planet, local);
        AssertThat(Math.Abs(global.X - 115_012.5)).IsLess(1e-6);
        var back = frames.ToPlanetLocal(_planet, global);
        AssertThat((double)System.Numerics.Vector3.Distance(local, back)).IsLess(1e-4);
        await scene.WaitFrames(1);
    }

    /// <summary>A system with the planet 115 km from the origin and a turned ship on it.</summary>
    private sealed class Source : IFramePoseSource
    {
        public PoseD Planet { get; init; } = new(new Vector3D(115_000, 0, 0), QuaternionD.Identity);

        public PoseD Ship { get; set; } = new(new Vector3D(10, 4010, -3), QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), 0.7));

        public bool TryGetParent(FrameRef frame, out FrameRef parent) => TryGetLocalPose(frame, out parent, out _);

        public bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent)
        {
            parent = default;
            poseInParent = PoseD.Identity;
            if (frame == FrameRef.Of(_system))
            {
                return true;
            }

            if (frame == FrameRef.Of(_planet))
            {
                parent = FrameRef.Of(_system);
                poseInParent = Planet;
                return true;
            }

            if (frame == _ship)
            {
                parent = FrameRef.Of(_planet);
                poseInParent = Ship;
                return true;
            }

            return false;
        }
    }
}
