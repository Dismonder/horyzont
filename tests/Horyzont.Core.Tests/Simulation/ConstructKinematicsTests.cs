using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Simulation;

public sealed class ConstructKinematicsTests
{
    private static readonly Quaternion _turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);

    [Fact]
    public void UpdateConstructKinematics_ShipConstruct_StoresPoseOutsideJournal()
    {
        var f = new SimFixture(state: TestShips.CreateDefault());
        var lastOp = f.State.LastOpId;
        var ship = Ship(f);

        Assert.True(f.Sim.UpdateConstructKinematics(TestShips.Moving, TestShips.HomeFrame, 0, new Vector3(5f, 4020f, -3f), _turned));

        Assert.Equal(new Vector3(5f, 4020f, -3f), ship.Origin);
        Assert.Equal(_turned, ship.Rotation);
        Assert.Equal(TestShips.HomeFrame, ship.Parent);
        Assert.Equal(0u, ship.ParentSeq);
        Assert.Empty(f.Journal.Entries);
        Assert.Equal(lastOp, f.State.LastOpId);
    }

    [Fact]
    public void UpdateConstructKinematics_StaticOrStaleSeq_ReturnsFalse()
    {
        var parked = new EntityId(3);
        var state = TestShips.CreateWorld(
            new TestShip(TestShips.Moving, TestShips.HomeFrame, new Vector3(0f, 4010f, 0f), Quaternion.Identity),
            new TestShip(parked, TestShips.HomeFrame, new Vector3(20f, 4010f, 0f), Quaternion.Identity, ConstructMode.Static));
        var f = new SimFixture(state: state);
        var ship = Ship(f);
        var target = new Vector3(1f, 4011f, 1f);

        // A static construct moves only through operations.
        Assert.False(f.Sim.UpdateConstructKinematics(parked, TestShips.HomeFrame, 0, target, Quaternion.Identity));
        f.State.Constructs.TryGet(parked, out var staticConstruct);
        Assert.Equal(new Vector3(20f, 4010f, 0f), staticConstruct!.Origin);

        // Stale frame or ParentSeq, unknown construct, bad values: nothing changes.
        Assert.False(f.Sim.UpdateConstructKinematics(TestShips.Moving, FrameRef.Of(new SystemId(1)), 0, target, Quaternion.Identity));
        Assert.False(f.Sim.UpdateConstructKinematics(TestShips.Moving, TestShips.HomeFrame, 1, target, Quaternion.Identity));
        Assert.False(f.Sim.UpdateConstructKinematics(new EntityId(99), TestShips.HomeFrame, 0, target, Quaternion.Identity));
        Assert.False(f.Sim.UpdateConstructKinematics(TestShips.Moving, TestShips.HomeFrame, 0, new Vector3(float.NaN, 0f, 0f), Quaternion.Identity));
        Assert.False(f.Sim.UpdateConstructKinematics(TestShips.Moving, TestShips.HomeFrame, 0, target, new Quaternion(0f, 0f, 0f, 2f)));
        Assert.Equal(new Vector3(0f, 4010f, 0f), ship.Origin);
        Assert.Equal(Quaternion.Identity, ship.Rotation);
        Assert.Empty(f.Journal.Entries);
    }

    [Fact]
    public void UpdateConstructKinematics_StateHashUnchanged_CheckpointHasPose()
    {
        var f = new SimFixture(state: TestShips.CreateDefault());
        f.State.CompleteCheckpoint(f.State.CaptureCheckpoint());
        var hash = StateHasher.Hash(f.State);
        var script = new ShipCircleScript(new Vector3(0f, 4010f, 0f), 100f);

        for (var i = 0; i < 30; i++)
        {
            f.Sim.AdvanceTick();
            Assert.True(script.Step(f.Sim, TestShips.Moving));
        }

        Assert.Equal(hash, StateHasher.Hash(f.State));
        var checkpoint = f.State.CaptureCheckpoint();
        var header = Assert.Single(checkpoint.UpsertedConstructs);
        Assert.Equal(TestShips.Moving, header.Id);
        Assert.Equal(ConstructMode.Ship, header.Mode);
        Assert.Equal(script.PoseAt(f.State.CurrentTick).Origin, header.Origin);
        Assert.Equal(script.PoseAt(f.State.CurrentTick).Rotation, header.Rotation);
        Assert.Empty(checkpoint.UpsertedChunks);
    }

    private static Construct Ship(SimFixture f) =>
        f.State.Constructs.TryGet(TestShips.Moving, out var ship) ? ship : throw new InvalidOperationException();
}
