using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Ships;

/// <summary>K5-25 decision 21: removing blocks under seated or standing players.</summary>
public sealed class ConstructNotEmptyTests
{
    private static EntityReparentOp Board(EntityId entity, EntityId construct, uint seq, CellPos? seat) =>
        new(entity, FrameRef.Of(construct), seq, new Vector3(0.5f, 1f, 0.5f), Quaternion.Identity, Vector3.Zero, seat);

    [Fact]
    public void ConstructNotEmpty_RemoveLastBlockWithSeatedOffline_Rejected38()
    {
        var f = new SimFixture();
        var p1 = f.Join(1);
        var p2 = f.Join(2);
        var c = f.BuildOnTerrain(p1, 2);
        f.Sim.ApplyServerOperation(Board(f.PlayerOf(p2).Entity, c, 1, new CellPos(0, 0, 0)));
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(p2, FrameRef.Of(c), 1, new Vector3(0.5f, 1f, 0.5f), Quaternion.Identity, 0f));

        Assert.Equal(RejectReason.ConstructNotEmpty, f.Sim.Submit(p1, new RemoveBlockIntent(c, new CellPos(0, 0, 0))).Reason);
    }

    [Fact]
    public void ConstructNotEmpty_WithStandingChild_Rejected38()
    {
        var f = new SimFixture();
        var p1 = f.Join(1);
        var p2 = f.Join(2);
        var c = f.BuildOnTerrain(p1, 2);
        f.Sim.ApplyServerOperation(Board(f.PlayerOf(p2).Entity, c, 1, null));

        Assert.Equal(RejectReason.ConstructNotEmpty, f.Sim.Submit(p1, new RemoveBlockIntent(c, new CellPos(0, 0, 0))).Reason);
    }

    [Fact]
    public void RemoveBlock_OccupiedSeat_RejectedSeatOccupied()
    {
        var f = new SimFixture();
        var p1 = f.Join(1);
        var p2 = f.Join(2);
        var c = f.BuildOnTerrain(p1, 2);
        f.SetBlock(c, new CellPos(0, 1, 0));
        f.Sim.ApplyServerOperation(Board(f.PlayerOf(p2).Entity, c, 1, new CellPos(0, 1, 0)));
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(p2, FrameRef.Of(c), 1, new Vector3(0.5f, 1f, 0.5f), Quaternion.Identity, 0f));

        Assert.Equal(RejectReason.SeatOccupied, f.Sim.Submit(p1, new RemoveBlockIntent(c, new CellPos(0, 1, 0))).Reason);
    }
}
