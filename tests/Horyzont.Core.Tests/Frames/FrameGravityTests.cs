using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Frames;

public class FrameGravityTests
{
    private static readonly PlanetParams _planet = new(new PlanetId(1), new SystemId(1), 1, "default");
    private static readonly FrameRef _planetFrame = FrameRef.Of(_planet.Id);
    private static readonly FrameRef _system = FrameRef.Of(new SystemId(1));
    private static readonly FrameRef _ship = FrameRef.Of(new EntityId(5));
    private static readonly FrameRef _inner = FrameRef.Of(new EntityId(6));

    [Theory]
    [InlineData(0f, 4000f, 0f)]
    [InlineData(1200f, -3800f, 300f)]
    [InlineData(0f, 4999f, 0f)]
    [InlineData(0f, 5001f, 0f)]
    public void FrameGravity_AtPlanet_MatchesTheMotor(float x, float y, float z)
    {
        var feet = new Vector3(x, y, z);
        var expected = CharacterMotor.GravityAt(feet, _planet);
        var gravity = FrameGravity.AtPlanet(_planet, Vector3D.FromFloat(feet));
        Assert.InRange(Vector3D.Distance(Vector3D.FromFloat(expected), gravity), 0, 1e-5);
    }

    [Fact]
    public void FrameGravity_ConstructFrame_IsRegionGravityInTheConstructFrame()
    {
        var shipPose = new PoseD(new Vector3D(30, 4100, -20), QuaternionD.FromAxisAngle(new Vector3D(1, 0, 0), 0.9));
        var innerPose = new PoseD(new Vector3D(2, 1, 0), QuaternionD.FromAxisAngle(new Vector3D(0, 0, 1), -0.4));
        var graph = new FrameGraph(new Source(shipPose, innerPose));
        var local = new Vector3D(1, 2, 3);

        Assert.True(FrameGravity.TryAt(graph, _inner, local, Planets, out var gravity));

        var inRegion = FrameMath.Compose(shipPose, innerPose);
        var point = FrameMath.TransformPoint(inRegion, local);
        var expected = FrameMath.InverseTransformDirection(inRegion, FrameGravity.AtPlanet(_planet, point));
        Assert.InRange(Vector3D.Distance(expected, gravity), 0, 1e-9);
        Assert.InRange(gravity.Length(), _planet.Gravity - 1e-4, _planet.Gravity + 1e-4);
    }

    [Fact]
    public void FrameGravity_PlanetFrameWithoutPlacement_UsesTheRegionOnly()
    {
        // The planet has no pose in the system (no placement): the gravity of a planet frame still works.
        var graph = new FrameGraph(new Source(PoseD.Identity, PoseD.Identity, placed: false));
        Assert.True(FrameGravity.TryAt(graph, _planetFrame, new Vector3D(0, 4000, 0), Planets, out var gravity));
        Assert.InRange(Vector3D.Distance(new Vector3D(0, -_planet.Gravity, 0), gravity), 0, 1e-5);
    }

    [Fact]
    public void FrameGravity_SystemFrame_IsZero_UnknownFrame_False()
    {
        var graph = new FrameGraph(new Source(PoseD.Identity, PoseD.Identity));
        Assert.True(FrameGravity.TryAt(graph, _system, new Vector3D(0, 4000, 0), Planets, out var gravity));
        Assert.Equal(Vector3D.Zero, gravity);
        Assert.False(FrameGravity.TryAt(graph, FrameRef.Of(new EntityId(99)), Vector3D.Zero, Planets, out _));
        Assert.False(FrameGravity.TryAt(graph, _planetFrame, Vector3D.Zero, _ => null, out _));
    }

    private static PlanetParams? Planets(PlanetId id) => id == _planet.Id ? _planet : null;

    private sealed class Source(PoseD ship, PoseD inner, bool placed = true) : IFramePoseSource
    {
        public bool TryGetParent(FrameRef frame, out FrameRef parent) => TryGet(frame, out parent, out _, requirePose: false);

        public bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent) =>
            TryGet(frame, out parent, out poseInParent, requirePose: true);

        private bool TryGet(FrameRef frame, out FrameRef parent, out PoseD pose, bool requirePose)
        {
            parent = default;
            pose = PoseD.Identity;
            if (frame == _system)
            {
                return true;
            }

            if (frame == _planetFrame)
            {
                parent = _system;
                return placed || !requirePose;
            }

            if (frame == _ship)
            {
                (parent, pose) = (_planetFrame, ship);
                return true;
            }

            if (frame == _inner)
            {
                (parent, pose) = (_ship, inner);
                return true;
            }

            return false;
        }
    }
}
