using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Frames;

public sealed class ServerFrameHistoryTests
{
    [Fact]
    public void ServerFrameHistory_ShipConstructs_Recorded500ms_ZeroAlloc()
    {
        var single = new EntityId(4);
        var state = TestShips.CreateWorld(
            new TestShip(TestShips.Moving, TestShips.HomeFrame, new Vector3(0f, 4010f, 0f), Quaternion.Identity),
            new TestShip(TestShips.Parked, TestShips.HomeFrame, new Vector3(50f, 4010f, 0f), Quaternion.Identity),
            new TestShip(new EntityId(3), TestShips.HomeFrame, new Vector3(-50f, 4010f, 0f), Quaternion.Identity, ConstructMode.Static),
            new TestShip(single, TestShips.HomeFrame, new Vector3(0f, 4010f, 50f), Quaternion.Identity, Size: 1));
        var rig = new FrameRig(state);
        Assert.Equal(ServerTickPhase.Compose, rig.History.Phase);

        rig.Tick(40);

        // Ship constructs only; a window of 31 ticks (500 ms).
        Assert.Equal(3, rig.History.Count);
        var last = rig.History.LastTick.Value;
        Assert.Equal(rig.State.CurrentTick.Value - 1, last);
        Assert.True(rig.History.History.TryGetWorldPose(FrameRef.Of(TestShips.Moving), last, out var now));
        Assert.Equal(rig.WorldPose(TestShips.Moving), now);
        Assert.True(rig.History.History.TryGetWorldPose(FrameRef.Of(TestShips.Moving), last - 30, out _));
        Assert.False(rig.History.History.TryGetWorldPose(FrameRef.Of(TestShips.Moving), last - 31, out _));
        Assert.False(rig.History.History.TryGetWorldPose(FrameRef.Of(new EntityId(3)), last, out _));

        // The velocity of the moving ship's origin is the 30 m/s of the script; the parked ship stands.
        var velocity = rig.History.PointVelocity(FrameRef.Of(TestShips.Moving), Vector3D.Zero, rig.TickSeconds);
        Assert.InRange(velocity.Length(), 29.9, 30.1);
        Assert.Equal(Vector3D.Zero, rig.History.PointVelocity(FrameRef.Of(TestShips.Parked), Vector3D.Zero, rig.TickSeconds));
        Assert.Equal(Vector3D.Zero, rig.History.PointVelocity(TestShips.HomeFrame, Vector3D.Zero, rig.TickSeconds));

        // A construct that disappears is forgotten.
        rig.Sim.ApplyServerOperation(new BlockClearOp(single, new CellPos(0, 0, 0)));
        rig.Tick();
        Assert.Equal(2, rig.History.Count);
        Assert.False(rig.History.History.TryGetWorldPose(FrameRef.Of(single), rig.History.LastTick.Value, out _));

        // Steady state: no allocations.
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            rig.History.Step(new Tick(rig.State.CurrentTick.Value + (ulong)i));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
