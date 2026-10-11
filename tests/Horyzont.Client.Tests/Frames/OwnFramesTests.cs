using System.Numerics;

using Horyzont.Client.Frames;
using Horyzont.Client.Movement;
using Horyzont.Client.Replication;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Frames;

public sealed class OwnFramesTests
{
    private static readonly EntityId _own = new(7);
    private static readonly PlanetId _planet = new(1);
    private static readonly FrameRef _home = FrameRef.Of(_planet);
    private static readonly FrameRef _ship = FrameRef.Of(new EntityId(5));

    private sealed class MovingShip : IFrameHistoryView
    {
        public static PoseD ShipAt(double tick) =>
            new(new Vector3D(0.5 * tick, 4000, 0), QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), 0.005 * tick));

        public bool TryGetWorldPose(FrameRef frame, double tick, out PoseD pose)
        {
            pose = frame == _ship ? ShipAt(tick) : PoseD.Identity;
            return frame == _ship || frame == _home;
        }
    }

    private static Snapshot Snap(uint id, ulong tick, FrameRef frame, uint seq, Vector3 position) =>
        new(id, new Tick(tick), 0, 0, [new SnapshotFrame(frame, seq)],
            [new EntityState(_own, (EntityStateFields)15, 0, seq, position, Quaternion.Identity, Vector3.Zero, (byte)MovementMode.Walking)]);

    [Fact]
    public void OwnPrediction_Reparent_RebasesIntoNewFrame_FrozenInConstruct()
    {
        var view = new MovingShip();
        var walking = new PredictedState(new Vector3(52f, 4001f, 3f), new Vector3(4f, 0f, 0f), Quaternion.Identity, 0.2f, MovementMode.Walking, true);
        Assert.True(OwnFrames.TryRebase(view, _home, _ship, 100, 1.0 / 60, walking, out var attached));

        // Same world point at the tick of the operation, held in the construct: frozen, no own motion.
        var world = FrameMath.TransformPoint(MovingShip.ShipAt(100), Vector3D.FromFloat(attached.Feet));
        Assert.InRange(Vector3D.Distance(Vector3D.FromFloat(walking.Feet), world), 0, 1e-3);
        Assert.Equal(MovementMode.Frozen, attached.Mode);
        Assert.Equal(Vector3.Zero, attached.Velocity);
        Assert.False(attached.OnFloor);
        Assert.Equal(0.2f, attached.Pitch);

        // Back on the planet: the world point of the attached state, walking again from the server state.
        Assert.True(OwnFrames.TryRebase(view, _ship, _home, 100, 1.0 / 60, attached, out var back));
        Assert.InRange(Vector3.Distance(walking.Feet, back.Feet), 0, 1e-3f);
        Assert.Equal(MovementMode.Frozen, back.Mode);

        // The attached body follows its frame between snapshots and its input is not simulated.
        var own = new OwnState(_planet, 1, attached.Feet, attached.BodyRotation, Vector3.Zero, MovementMode.Walking, 0, 1, new Tick(100)) { Frame = _ship };
        Assert.True(OwnFrames.IsAttached(own));
        Assert.False(OwnFrames.SimulatesInput(own));
        Assert.True(OwnFrames.SimulatesInput(own with { Frame = _home }));
        Assert.False(OwnFrames.SimulatesInput(own with { Frame = _home, Mode = MovementMode.Frozen }));
        double previous = 0;
        for (var tick = 100.0; tick < 101; tick += 1.0 / 6)
        {
            Assert.True(OwnFrames.TryGetWorldPose(view, own, tick, out var pose));
            var expected = FrameMath.TransformPoint(MovingShip.ShipAt(tick), Vector3D.FromFloat(attached.Feet));
            Assert.InRange(Vector3D.Distance(expected, pose.Position), 0, 1e-9);
            if (previous > 0)
            {
                Assert.True(pose.Position.X > previous);
            }

            previous = pose.Position.X;
        }
    }

    [Fact]
    public void OwnStateValidator_ConstructFrame_AcceptedAfterAPlanetWithItsFrame()
    {
        var validator = new OwnStateValidator(_own, [_planet], NullLogger<OwnStateValidator>.Instance);
        Assert.Equal(OwnStateVerdict.UnknownPlanet, validator.Validate(Snap(1, 100, _ship, 1, Vector3.Zero), out _));
        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(2, 101, _home, 0, new Vector3(0, 4001, 0)), out var onPlanet));
        Assert.Equal(_home, onPlanet.Frame);
        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(3, 102, _ship, 1, new Vector3(1, 2, 3)), out var inShip));
        Assert.Equal(_ship, inShip.Frame);
        Assert.Equal(_planet, inShip.Planet);
        Assert.Equal(1u, inShip.ParentSeq);
        Assert.Equal(OwnStateVerdict.UnknownPlanet, validator.Validate(Snap(4, 103, FrameRef.Of(new SystemId(1)), 2, Vector3.Zero), out _));
    }

    [Fact]
    public void OwnStateValidator_FirstStateInConstructFrame_PlanetFromFrameRegion()
    {
        // Reconnecting while saved in a ship: nothing accepted yet, the planet comes from the region of the construct frame.
        var graph = new FrameGraph(new ShipOnHome());
        var validator = new OwnStateValidator(_own, [_planet], NullLogger<OwnStateValidator>.Instance);
        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(1, 100, _ship, 1, new Vector3(1, 2, 3)), out var inShip, graph));
        Assert.Equal(_ship, inShip.Frame);
        Assert.Equal(_planet, inShip.Planet);

        // After a reset (new session) the same applies; a frame whose region is not a known planet is still refused.
        validator.Reset();
        Assert.Equal(OwnStateVerdict.Accepted, validator.Validate(Snap(1, 100, _ship, 1, Vector3.Zero), out _, graph));
        var stray = new OwnStateValidator(_own, [new PlanetId(2)], NullLogger<OwnStateValidator>.Instance);
        Assert.Equal(OwnStateVerdict.UnknownPlanet, stray.Validate(Snap(1, 100, _ship, 1, Vector3.Zero), out _, graph));
        Assert.Equal(OwnStateVerdict.UnknownPlanet, stray.Validate(Snap(2, 101, FrameRef.Of(new EntityId(9)), 1, Vector3.Zero), out _, graph));
    }

    private sealed class ShipOnHome : IFramePoseSource
    {
        public bool TryGetParent(FrameRef frame, out FrameRef parent) => TryGetLocalPose(frame, out parent, out _);

        public bool TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent)
        {
            poseInParent = PoseD.Identity;
            parent = frame == _ship ? _home : frame == _home ? FrameRef.Of(new SystemId(1)) : default;
            return frame == _ship || frame == _home || frame == FrameRef.Of(new SystemId(1));
        }
    }
}

public sealed class ClientFramesTests
{
    [Fact]
    public void ClientFrames_BuiltAtFirstHello_FedAndClearedPerSession()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var entities = new EntityMirror(time);
        var frames = new ClientFrames(entities, new ConstructMirror(time));
        var ship = new EntityId(5);
        Assert.False(frames.TryGetWorldPose(FrameRef.Of(new PlanetId(1)), 0, out _));
        Assert.Null(frames.Graph);

        var planet = new Horyzont.Core.World.PlanetParams(new PlanetId(1), new SystemId(1), 7, "core:test/1");
        frames.OnServerHello(new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), [planet], [], []));
        var graph = frames.Graph;
        Assert.NotNull(graph);
        Assert.True(frames.TryGetWorldPose(FrameRef.Of(new PlanetId(1)), 0, out var home));
        Assert.Equal(PoseD.Identity, home);

        entities.Apply(new EntitySpawn(ship, EntityKind.Construct, 0, null, new SnapshotFrame(FrameRef.Of(new PlanetId(1)), 0),
            new EntityState(ship, (EntityStateFields)15, 0, 0, new Vector3(1, 2, 3), Quaternion.Identity, Vector3.Zero, 0), []));
        var full = new Snapshot(1, new Tick(10), 0, 0, [new SnapshotFrame(FrameRef.Of(new PlanetId(1)), 0)],
            [new EntityState(ship, (EntityStateFields)15, 0, 0, new Vector3(1, 2, 3), Quaternion.Identity, Vector3.Zero, 0)]);
        entities.OnFullSnapshot(full);
        frames.OnFullSnapshot(full);
        Assert.True(frames.TryGetWorldPose(FrameRef.Of(ship), 10, out var pose));
        Assert.Equal(new Vector3D(1, 2, 3), pose.Position);

        frames.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.ServerShutdown);
        Assert.False(frames.TryGetWorldPose(FrameRef.Of(ship), 10, out _));
        frames.OnServerHello(new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), [planet], [], []));
        Assert.Same(graph, frames.Graph);
    }
}
