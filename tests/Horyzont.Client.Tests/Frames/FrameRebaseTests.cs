using System.Numerics;

using Horyzont.Client.Frames;
using Horyzont.Client.Movement;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;

namespace Horyzont.Client.Tests.Frames;

public sealed class FrameRebaseTests
{
    private static readonly EntityId _entity = new(9);
    private static readonly FrameRef _home = FrameRef.Of(new PlanetId(1));
    private static readonly FrameRef _ship = FrameRef.Of(new EntityId(5));
    private static readonly Vector3D _rest = new(10, 2, -3);

    /// <summary>Planet in identity; a ship moving 30 m/s along X and turning 0.3 rad/s, recorded from <see cref="From"/>.</summary>
    private sealed class MovingShip(double from = 0) : IFrameHistoryView
    {
        public double From { get; } = from;

        public static PoseD ShipAt(double tick) =>
            new(new Vector3D(0.5 * tick, 0, 0), QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), 0.005 * tick));

        public bool TryGetWorldPose(FrameRef frame, double tick, out PoseD pose)
        {
            pose = PoseD.Identity;
            if (frame == _home)
            {
                return true;
            }

            if (frame != _ship || tick < From)
            {
                return false;
            }

            pose = ShipAt(tick);
            return true;
        }
    }

    private static RemoteMotionSample InPlanet(ulong tick) =>
        new(_entity, _home, 0, new Tick(tick), ToFloat(_rest), Quaternion.Identity, Vector3.Zero, (byte)MovementMode.Walking);

    private static RemoteMotionSample InShip(ulong tick)
    {
        var local = FrameMath.Relative(MovingShip.ShipAt(tick), new PoseD(_rest, QuaternionD.Identity));
        Assert.True(FrameMath.TryToFloat(local, FrameKind.Construct, out var position, out var rotation));
        return new RemoteMotionSample(_entity, _ship, 1, new Tick(tick), position, rotation, Vector3.Zero, (byte)MovementMode.Frozen);
    }

    private static Vector3 ToFloat(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static Vector3D WorldOf(in RemoteMotionSample sample, double tick)
    {
        Assert.True(new MovingShip().TryGetWorldPose(sample.Frame, tick, out var frame));
        return FrameMath.TransformPoint(frame, Vector3D.FromFloat(sample.Position));
    }

    [Fact]
    public void RemoteMotionBuffer_ReparentWithHistory_RebasesWithoutJump()
    {
        var buffer = new RemoteMotionBuffer(frames: new MovingShip());
        for (ulong tick = 100; tick <= 110; tick += 2)
        {
            Assert.True(buffer.Push(InPlanet(tick)));
        }

        Assert.True(buffer.Push(InShip(112)));
        Assert.Equal(7, buffer.Count);
        Assert.Equal(0, buffer.ReparentSnaps);
        for (var tick = 100.0; tick <= 112.0; tick += 0.25)
        {
            Assert.True(buffer.TrySample(tick, out var sample));
            Assert.Equal(_ship, sample.Frame);
            Assert.Equal(1u, sample.ParentSeq);
            Assert.True(Vector3D.Distance(_rest, WorldOf(sample, tick)) < 0.01, $"tick {tick}: {WorldOf(sample, tick)}");
        }
    }

    [Fact]
    public void RemoteMotionBuffer_ReparentWithHistory_VelocityStaysWorldVelocity()
    {
        var buffer = new RemoteMotionBuffer(frames: new MovingShip());
        buffer.Push(InPlanet(100));
        buffer.Push(InShip(102) with { MotionFlags = (byte)MovementMode.Walking });
        Assert.True(buffer.TrySample(100, out var rebased));

        // At rest in the world: the local velocity cancels the ship point velocity (backward difference over one tick).
        var ship = MovingShip.ShipAt(100);
        var local = Vector3D.FromFloat(rebased.Position);
        var pointVelocity = (FrameMath.TransformPoint(ship, local) - FrameMath.TransformPoint(MovingShip.ShipAt(99), local)) * 60;
        var world = pointVelocity + FrameMath.TransformDirection(ship, Vector3D.FromFloat(rebased.Velocity));
        Assert.True(world.Length() < 0.05, world.ToString());
    }

    [Fact]
    public void RemoteMotionBuffer_LeavingAFastShip_IsNotATeleport()
    {
        var buffer = new RemoteMotionBuffer(frames: new MovingShip());
        for (ulong tick = 100; tick <= 106; tick += 2)
        {
            Assert.True(buffer.Push(InShip(tick)));
        }

        // Off the ship 6 ticks later: 3 m away from the last sample in the planet frame, at the ship's speed.
        var world = FrameMath.TransformPoint(MovingShip.ShipAt(112), Vector3D.FromFloat(InShip(106).Position));
        Assert.True(buffer.Push(InPlanet(112) with { Position = new Vector3((float)world.X, (float)world.Y, (float)world.Z), ParentSeq = 2 }));
        Assert.Equal(5, buffer.Count);
        Assert.Equal(0, buffer.ReparentSnaps);
    }

    [Fact]
    public void RemoteMotionBuffer_ReparentWithoutHistory_DropsOlderAndCountsSnap()
    {
        var buffer = new RemoteMotionBuffer(frames: new MovingShip(from: 106));
        for (ulong tick = 100; tick <= 110; tick += 2)
        {
            buffer.Push(InPlanet(tick));
        }

        Assert.True(buffer.Push(InShip(112)));
        Assert.Equal(4, buffer.Count);
        Assert.Equal(1, buffer.ReparentSnaps);
        Assert.True(buffer.TrySample(100, out var oldest));
        Assert.Equal(106u, oldest.ServerTick.Value);
        Assert.True(Vector3D.Distance(_rest, WorldOf(oldest, 106)) < 0.01);
    }

    [Fact]
    public void RemoteMotionBuffer_ParentChangeWithoutFrames_StillResets()
    {
        var buffer = new RemoteMotionBuffer();
        buffer.Push(InPlanet(100));
        buffer.Push(InPlanet(102));
        Assert.True(buffer.Push(InShip(104)));
        Assert.Equal(1, buffer.Count);
        Assert.Equal(1, buffer.ReparentSnaps);
    }

    [Fact]
    public void FrameRebase_SameWorldPointAtTick_AndAllocatesNothing()
    {
        var view = new MovingShip();
        var sample = InPlanet(100);
        Assert.True(FrameRebase.TryRebase(view, _home, _ship, 100, 1.0 / 60, sample.Position, sample.Rotation, sample.Velocity,
            out var position, out _, out _));
        Assert.True(Vector3D.Distance(_rest, FrameMath.TransformPoint(MovingShip.ShipAt(100), Vector3D.FromFloat(position))) < 0.01);
        Assert.False(FrameRebase.TryRebase(new MovingShip(from: 200), _home, _ship, 100, 1.0 / 60, sample.Position, sample.Rotation,
            sample.Velocity, out _, out _, out _));

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                FrameRebase.TryRebase(view, _home, _ship, 100 + (i % 10), 1.0 / 60, sample.Position, sample.Rotation, sample.Velocity,
                    out _, out _, out _);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
    }
}
