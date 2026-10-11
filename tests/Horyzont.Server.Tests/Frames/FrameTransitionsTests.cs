using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Frames;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Frames;

public sealed class FrameTransitionsTests
{
    private const double MaxPoseError = 1e-3;
    private const double MaxVelocityError = 0.01;

    private static readonly FrameRef _system = FrameRef.Of(new SystemId(1));

    [Fact]
    public void FrameTransitions_PlanetToMovingConstructAndBack_WorldPoseWithin1mm()
    {
        var rig = new FrameRig();
        var player = rig.Join();
        rig.Tick(20);

        // Planet -> moving ship (30 m/s on a circle, turning 0.3 rad/s), with a seat in the ship floor.
        var before = rig.WorldPose(player);
        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(TestShips.Moving), new CellPos(1, 0, 1), out var applied, out var failure));
        Assert.Equal(FrameTransitionFailure.None, failure);
        var op = Assert.IsType<EntityReparentOp>(applied!.Operation);
        Assert.Equal(FrameRef.Of(TestShips.Moving), player.Frame);
        Assert.Equal(1u, player.ParentSeq);
        Assert.Equal(new CellPos(1, 0, 1), player.Seat);
        AssertSamePose(before, rig.WorldPose(player));

        // The player stood still in the world: in the ship it moves against the ship's point velocity.
        var shipPose = rig.WorldPose(TestShips.Moving);
        var worldVelocity = FrameMath.TransformDirection(shipPose, Vector3D.FromFloat(op.LocalVelocity)) + rig.RestVelocity(player);
        Assert.InRange(worldVelocity.Length(), 0, MaxVelocityError);
        Assert.InRange(rig.RestVelocity(player).Length(), 25, 60);

        // Ride along, then back to the planet: same world pose, world velocity = the ship point velocity.
        rig.Tick(45);
        before = rig.WorldPose(player);
        var riding = rig.RestVelocity(player);
        Assert.True(rig.Transitions.TryReparent(player.Entity, TestShips.HomeFrame, null, out applied, out failure));
        op = Assert.IsType<EntityReparentOp>(applied!.Operation);
        Assert.Equal(TestShips.HomeFrame, player.Frame);
        Assert.Equal(2u, player.ParentSeq);
        Assert.Null(player.Seat);
        AssertSamePose(before, rig.WorldPose(player));
        Assert.InRange(Vector3D.Distance(riding, Vector3D.FromFloat(op.LocalVelocity)), 0, MaxVelocityError);
        Assert.Equal(3, rig.Journal.Entries.Count);
    }

    [Fact]
    public void FrameTransitions_ConstructToSystem_WorldPoseWithin1mm()
    {
        var rig = new FrameRig();
        rig.Tick(30);
        var velocity = rig.History.PointVelocity(FrameRef.Of(TestShips.Moving), Vector3D.Zero, rig.TickSeconds);

        var before = rig.WorldPose(TestShips.Moving);
        Assert.True(rig.Transitions.TryReparent(TestShips.Moving, _system, null, out var applied, out _));
        var op = Assert.IsType<EntityReparentOp>(applied!.Operation);
        rig.State.Constructs.TryGet(TestShips.Moving, out var ship);
        Assert.Equal(_system, ship!.Parent);
        Assert.Equal(1u, ship.ParentSeq);
        AssertSamePose(before, rig.WorldPose(TestShips.Moving));
        Assert.InRange(Vector3D.Distance(velocity, Vector3D.FromFloat(op.LocalVelocity)), 0, MaxVelocityError);

        // The ship keeps moving in the system frame and comes back to the planet.
        rig.Tick(10);
        before = rig.WorldPose(TestShips.Moving);
        Assert.True(rig.Transitions.TryReparent(TestShips.Moving, TestShips.HomeFrame, null, out _, out _));
        Assert.Equal(TestShips.HomeFrame, ship.Parent);
        Assert.Equal(2u, ship.ParentSeq);
        AssertSamePose(before, rig.WorldPose(TestShips.Moving));
    }

    [Fact]
    public void FrameTransitions_RejectsPlayerToSystemAndStaticConstruct()
    {
        var state = TestShips.CreateWorld(
            new TestShip(TestShips.Moving, TestShips.HomeFrame, new Vector3(0f, 4010f, 0f), Quaternion.Identity),
            new TestShip(TestShips.Parked, TestShips.HomeFrame, new Vector3(50f, 4010f, 0f), Quaternion.Identity, ConstructMode.Static),
            new TestShip(new EntityId(3), _system, new Vector3(300_000f, 0f, 0f), Quaternion.Identity));
        var rig = new FrameRig(state);
        var player = rig.Join();
        rig.Tick(5);
        var entries = rig.Journal.Entries.Count;

        Assert.Equal(FrameTransitionFailure.PlayerToSystem, Fail(rig, player.Entity, _system, null));
        Assert.Equal(FrameTransitionFailure.ConstructNotShip, Fail(rig, TestShips.Parked, _system, null));
        Assert.Equal(FrameTransitionFailure.ConstructIntoConstruct, Fail(rig, TestShips.Moving, FrameRef.Of(TestShips.Parked), null));
        Assert.Equal(FrameTransitionFailure.UnknownEntity, Fail(rig, new EntityId(99), TestShips.HomeFrame, null));
        Assert.Equal(FrameTransitionFailure.UnknownFrame, Fail(rig, player.Entity, FrameRef.Of(new PlanetId(9)), null));
        Assert.Equal(FrameTransitionFailure.UnknownFrame, Fail(rig, TestShips.Moving, FrameRef.Of(new SystemId(2)), null));
        Assert.Equal(FrameTransitionFailure.InvalidSeat, Fail(rig, player.Entity, FrameRef.Of(TestShips.Moving), new CellPos(0, 3, 0)));
        Assert.Equal(FrameTransitionFailure.InvalidSeat, Fail(rig, player.Entity, TestShips.HomeFrame, new CellPos(0, 0, 0)));
        Assert.Equal(FrameTransitionFailure.InvalidSeat, Fail(rig, TestShips.Moving, _system, new CellPos(0, 0, 0)));

        // 300 km out in the system frame does not fit the 100 km float range of the planet frame.
        Assert.Equal(FrameTransitionFailure.OutOfRange, Fail(rig, new EntityId(3), TestShips.HomeFrame, null));

        // A taken seat.
        var other = rig.Join(2);
        Assert.True(rig.Transitions.TryReparent(other.Entity, FrameRef.Of(TestShips.Moving), new CellPos(0, 0, 0), out _, out _));
        entries = rig.Journal.Entries.Count;
        Assert.Equal(FrameTransitionFailure.InvalidSeat, Fail(rig, player.Entity, FrameRef.Of(TestShips.Moving), new CellPos(0, 0, 0)));

        // A player may enter a static construct (P7: only through a reparent).
        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(TestShips.Parked), null, out _, out _));
        Assert.Equal(entries + 1, rig.Journal.Entries.Count);
    }

    private static FrameTransitionFailure Fail(FrameRig rig, EntityId entity, FrameRef frame, CellPos? seat)
    {
        var entries = rig.Journal.Entries.Count;
        Assert.False(rig.Transitions.TryReparent(entity, frame, seat, out var applied, out var failure));
        Assert.Null(applied);
        Assert.Equal(entries, rig.Journal.Entries.Count);
        return failure;
    }

    private static void AssertSamePose(in PoseD expected, in PoseD actual)
    {
        Assert.InRange(Vector3D.Distance(expected.Position, actual.Position), 0, MaxPoseError);
        Assert.InRange(Math.Abs(QuaternionD.Dot(expected.Rotation, actual.Rotation)), 1 - 1e-6, 1 + 1e-6);
    }
}
