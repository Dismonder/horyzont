using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Simulation;

public sealed class ReparentTests
{
    private static readonly FrameRef _planetFrame = FrameRef.Of(SimFixture.Planet);

    private static EntityReparentOp Board(EntityId entity, EntityId construct, uint seq, CellPos? seat) =>
        new(entity, FrameRef.Of(construct), seq, new Vector3(0.5f, 1f, 0.5f), Quaternion.Identity, Vector3.Zero, seat);

    [Fact]
    public void Reparent_PlayerIntoSeatAndBack_UpdatesFrameSeqAndSeat()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);
        var player = f.PlayerOf(p);

        f.Sim.ApplyServerOperation(Board(player.Entity, c, 1, new CellPos(0, 0, 0)));

        Assert.Equal(FrameRef.Of(c), player.Frame);
        Assert.Equal(1u, player.ParentSeq);
        Assert.Equal(new CellPos(0, 0, 0), player.Seat);

        f.Sim.ApplyServerOperation(new EntityReparentOp(
            player.Entity, _planetFrame, 2, f.Ground(0) + Vector3.UnitY, Quaternion.Identity, Vector3.UnitX, null));

        Assert.Equal(_planetFrame, player.Frame);
        Assert.Equal(2u, player.ParentSeq);
        Assert.Null(player.Seat);
    }

    [Fact]
    public void Reparent_BrokenPreconditions_Throw()
    {
        var f = new SimFixture();
        var p = f.Join();
        var a = f.BuildOnTerrain(p, 2);
        var b = f.BuildOnTerrain(p, -2);
        var entity = f.PlayerOf(p).Entity;

        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(Board(entity, a, 2, null)));
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(Board(entity, new EntityId(999), 1, null)));
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(Board(entity, a, 1, new CellPos(0, 5, 0))));
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(Board(a, a, 1, null)));
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(Board(a, b, 1, new CellPos(0, 0, 0))));

        f.Sim.ApplyServerOperation(Board(b, a, 1, null));
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(Board(a, b, 1, null)));
        Assert.Equal(FrameRef.Of(a), f.State.Constructs.All.Single(x => x.Id == b).Parent);
    }

    [Fact]
    public void Reparent_SeatTakenByOfflinePlayer_Throws()
    {
        var f = new SimFixture();
        var p1 = f.Join(1);
        var p2 = f.Join(2);
        var c = f.BuildOnTerrain(p1, 2);
        f.Sim.ApplyServerOperation(Board(f.PlayerOf(p1).Entity, c, 1, new CellPos(0, 0, 0)));
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(p1, FrameRef.Of(c), 1, new Vector3(0.5f, 1f, 0.5f), Quaternion.Identity, 0f));

        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(Board(f.PlayerOf(p2).Entity, c, 1, new CellPos(0, 0, 0))));
    }

    [Fact]
    public void BlockClear_SeatCell_Rejected()
    {
        var f = new SimFixture();
        var p1 = f.Join(1);
        var p2 = f.Join(2);
        var c = f.BuildOnTerrain(p1, 2);
        f.SetBlock(c, new CellPos(0, 1, 0));
        f.Sim.ApplyServerOperation(Board(f.PlayerOf(p2).Entity, c, 1, new CellPos(0, 1, 0)));

        Assert.Equal(RejectReason.SeatOccupied, f.Sim.Submit(p1, new RemoveBlockIntent(c, new CellPos(0, 1, 0))).Reason);
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(new BlockClearOp(c, new CellPos(0, 1, 0))));
    }

    [Fact]
    public void BlockClear_LastBlockOfFrameWithChildren_Rejected()
    {
        var f = new SimFixture();
        var p1 = f.Join(1);
        var p2 = f.Join(2);
        var c = f.BuildOnTerrain(p1, 2);
        f.Sim.ApplyServerOperation(Board(f.PlayerOf(p2).Entity, c, 1, null));

        Assert.Equal(RejectReason.ConstructNotEmpty, f.Sim.Submit(p1, new RemoveBlockIntent(c, new CellPos(0, 0, 0))).Reason);
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(new BlockClearOp(c, new CellPos(0, 0, 0))));
    }

    [Fact]
    public void UpdatePlayerKinematics_StaleFrameOrSeq_ReturnsFalse()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);
        var position = f.PlayerOf(p).Position;
        f.Sim.ApplyServerOperation(Board(f.PlayerOf(p).Entity, c, 1, null));

        Assert.False(f.Sim.UpdatePlayerKinematics(p, _planetFrame, 0, position, Quaternion.Identity, 0));
        Assert.False(f.Sim.UpdatePlayerKinematics(p, FrameRef.Of(c), 0, position, Quaternion.Identity, 0));
        Assert.True(f.Sim.UpdatePlayerKinematics(p, FrameRef.Of(c), 1, new Vector3(0.5f, 1.2f, 0.5f), Quaternion.Identity, 0));
    }
}
