using System.Numerics;

using Horyzont.Client.Frames;
using Horyzont.Client.Replication;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Frames;

public sealed class ClientFrameGraphTests
{
    private static readonly PlanetId _home = new(1);
    private static readonly SystemId _system = new(1);
    private static readonly FrameRef _homeFrame = FrameRef.Of(_home);
    private static readonly EntityId _ship = new(5);
    private static readonly EntityId _still = new(6);
    private static readonly EntityId _onShip = new(7);
    private static readonly Quaternion _shipTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);

    private sealed class Rig
    {
        public Rig()
        {
            var time = new FakeTimeProvider();
            Entities = new EntityMirror(time);
            Constructs = new ConstructMirror(time);
            var placement = new HomeOnlyPlacement(_home);
            History = new ClientFrameHistory(Entities, placement);
            Graph = new ClientFrameGraph(Entities, Constructs, History, placement, _system);
        }

        public EntityMirror Entities { get; }

        public ConstructMirror Constructs { get; }

        public ClientFrameHistory History { get; }

        public ClientFrameGraph Graph { get; }

        public void AddConstruct(EntityId id, FrameRef parent, ConstructMode mode, Vector3 origin, Quaternion rotation)
        {
            Entities.Apply(new EntitySpawn(id, EntityKind.Construct, 0, null, new SnapshotFrame(parent, 0),
                State(id, origin, rotation), []));
            Assert.True(Constructs.Apply(new ConstructState(new ConstructHeaderRecord(id, parent, 0, mode, origin, rotation, 1), 0, 1, [])));
        }

        /// <summary>A full snapshot with the ship at a server tick, given to the mirror and the history like the decoder does.</summary>
        public void ShipAt(ulong tick, Vector3 origin, Quaternion rotation)
        {
            var full = new Snapshot((uint)tick, new Tick(tick), 0, 0, [new SnapshotFrame(_homeFrame, 0)], [State(_ship, origin, rotation)]);
            Entities.OnFullSnapshot(full);
            History.OnFullSnapshot(full);
        }
    }

    private static EntityState State(EntityId id, Vector3 position, Quaternion rotation, uint seq = 0) =>
        new(id, (EntityStateFields)15, 0, seq, position, rotation, Vector3.Zero, 0);

    private static Rig Ships()
    {
        var rig = new Rig();
        rig.AddConstruct(_ship, _homeFrame, ConstructMode.Ship, new Vector3(0f, 4010f, 0f), Quaternion.Identity);
        rig.AddConstruct(_still, _homeFrame, ConstructMode.Static, new Vector3(100f, 4000f, 0f), Quaternion.Identity);
        rig.AddConstruct(_onShip, FrameRef.Of(_ship), ConstructMode.Static, new Vector3(1f, 2f, 3f), Quaternion.Identity);
        rig.ShipAt(30, new Vector3(0f, 4010f, 0f), Quaternion.Identity);
        rig.ShipAt(33, new Vector3(3f, 4010f, 0f), _shipTurn);
        return rig;
    }

    [Fact]
    public void ClientFrameGraph_ShipPoseFromSnapshots_InterpolatedAtRenderTick()
    {
        var rig = Ships();

        // Ship: interpolated between the snapshot ticks.
        Assert.True(rig.Graph.TryGetWorldPose(FrameRef.Of(_ship), 31.5, out var ship));
        Assert.InRange(Vector3D.Distance(new Vector3D(1.5, 4010, 0), ship.Position), 0, 1e-6);
        var halfTurn = QuaternionD.FromFloat(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.15f));
        Assert.InRange(Math.Abs(QuaternionD.Dot(halfTurn, ship.Rotation)), 1 - 1e-9, 1 + 1e-9);

        // Static construct: its header, whatever the render tick.
        Assert.True(rig.Graph.TryGetWorldPose(FrameRef.Of(_still), 31.5, out var still));
        Assert.Equal(new Vector3D(100, 4000, 0), still.Position);

        // A static construct on the ship follows the interpolated ship.
        Assert.True(rig.Graph.TryGetWorldPose(FrameRef.Of(_onShip), 31.5, out var onShip));
        var expected = FrameMath.Compose(ship, FrameMath.FromFloat(new Vector3(1f, 2f, 3f), Quaternion.Identity));
        Assert.InRange(Vector3D.Distance(expected.Position, onShip.Position), 0, 1e-9);

        // Past the newest snapshot: the newest ship pose (no extrapolation).
        Assert.True(rig.Graph.TryGetWorldPose(FrameRef.Of(_ship), 40, out var newest));
        Assert.InRange(Vector3D.Distance(new Vector3D(3, 4010, 0), newest.Position), 0, 1e-6);

        // Planets and the system come from the placement; the region of a construct is its planet.
        Assert.True(rig.Graph.TryGetWorldPose(_homeFrame, 31.5, out var planet));
        Assert.Equal(PoseD.Identity, planet);
        Assert.True(rig.Graph.Graph.TryGetRegion(FrameRef.Of(_onShip), out var region));
        Assert.Equal(_homeFrame, region);
        Assert.False(rig.Graph.TryGetWorldPose(FrameRef.Of(new EntityId(99)), 31.5, out _));
        Assert.False(rig.Graph.TryGetWorldPose(FrameRef.Of(new PlanetId(2)), 31.5, out _));
    }

    [Fact]
    public void ClientFrameGraph_NewestPoses_ShipFromMirrorStaticFromHeader()
    {
        var rig = Ships();
        Assert.True(rig.Graph.Graph.TryGetWorldPose(FrameRef.Of(_ship), out var ship));
        Assert.InRange(Vector3D.Distance(new Vector3D(3, 4010, 0), ship.Position), 0, 1e-6);
        Assert.True(rig.Graph.Graph.TryGetWorldPose(FrameRef.Of(_still), out var still));
        Assert.Equal(new Vector3D(100, 4000, 0), still.Position);
    }

    [Fact]
    public void ClientFrameHistory_RecordsShipsOnly_ForgetsDespawned()
    {
        var rig = Ships();
        Assert.True(rig.History.Contains(FrameRef.Of(_ship)));
        Assert.False(rig.History.Contains(FrameRef.Of(_still)));
        Assert.True(rig.History.TryGetWorldPose(FrameRef.Of(_ship), 30, out var first));
        Assert.Equal(new Vector3D(0, 4010, 0), first.Position);

        // A ship that reparents keeps its world poses: the history records world, not local, poses.
        rig.Entities.Apply(new OperationApplied(new OpId(1), new Tick(34),
            new EntityReparentOp(_ship, FrameRef.Of(_system), 1, new Vector3(3f, 4010f, 0f), _shipTurn, Vector3.Zero, null), []));
        var full = new Snapshot(36, new Tick(36), 0, 0, [new SnapshotFrame(FrameRef.Of(_system), 1)],
            [State(_ship, new Vector3(3f, 4010f, 0f), _shipTurn, 1)]);
        rig.Entities.OnFullSnapshot(full);
        rig.History.OnFullSnapshot(full);
        Assert.True(rig.History.TryGetWorldPose(FrameRef.Of(_ship), 34.5, out var across));
        Assert.InRange(Vector3D.Distance(new Vector3D(3, 4010, 0), across.Position), 0, 1e-6);

        rig.Entities.Apply(new EntityDespawn(_ship));
        Assert.False(rig.History.Contains(FrameRef.Of(_ship)));
    }

    [Fact]
    public void ClientFrameGraph_RenderTickQuery_AllocatesZeroBytes()
    {
        var rig = Ships();
        var sink = 0.0;
        for (var i = 0; i < 100; i++)
        {
            rig.Graph.TryGetWorldPose(FrameRef.Of(_onShip), 31 + (i % 3), out var pose);
            sink += pose.Position.X;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            rig.Graph.TryGetWorldPose(FrameRef.Of(_onShip), 30 + (i % 4), out var pose);
            sink += pose.Position.X;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(double.IsFinite(sink));
    }

    [Fact]
    public void EntityMirror_SampleInOtherFrameWithKnownSeq_CountsWrongFrameApplied()
    {
        var mirror = new EntityMirror(new FakeTimeProvider());
        var player = new EntityId(9);
        mirror.Apply(new EntitySpawn(player, EntityKind.Player, 0, new PlayerId(1), new SnapshotFrame(_homeFrame, 0),
            State(player, new Vector3(0, 4000, 0), Quaternion.Identity), []));

        // A reparent followed by samples in the new frame is the normal path.
        mirror.Apply(new OperationApplied(new OpId(1), new Tick(10),
            new EntityReparentOp(player, FrameRef.Of(_ship), 1, Vector3.One, Quaternion.Identity, Vector3.Zero, null), []));
        mirror.OnFullSnapshot(new Snapshot(1, new Tick(12), 0, 0, [new SnapshotFrame(FrameRef.Of(_ship), 1)],
            [State(player, Vector3.One, Quaternion.Identity, 1)]));
        Assert.Equal(0, mirror.WrongFrameApplied);

        // The same sequence in another frame contradicts the known frame.
        mirror.OnFullSnapshot(new Snapshot(2, new Tick(15), 0, 0, [new SnapshotFrame(_homeFrame, 1)],
            [State(player, Vector3.One, Quaternion.Identity, 1)]));
        Assert.Equal(1, mirror.WrongFrameApplied);
    }
}
