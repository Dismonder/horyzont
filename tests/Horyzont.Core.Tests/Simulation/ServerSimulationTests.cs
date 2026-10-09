using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Simulation;

public sealed class ServerSimulationTests
{
    [Fact]
    public void Submit_Accepted_JournalsConsecutiveOpIds()
    {
        var f = new SimFixture();
        var p = f.Join();

        var dig = f.Sim.Submit(p, f.Dig(p, f.Ground(2)));
        var rejected = f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { RayDirection = Vector3.UnitY });
        f.Ticks(3);
        var place = f.Sim.Submit(p, f.PlaceOnTerrain(p, f.Ground(3)));

        Assert.False(rejected.IsAccepted);
        Assert.Equal([new OpId(1), new OpId(2), new OpId(3)], f.Journal.Entries.Select(e => e.Id));
        Assert.Equal(new OpId(2), dig.Applied!.Id);
        Assert.Equal(new OpId(3), place.Applied!.Id);
        Assert.Null(f.Journal.Entries[0].Actor);
        Assert.Equal(p, f.Journal.Entries[1].Actor);
        Assert.Equal(new Tick(3), f.Journal.Entries[2].Tick);
        Assert.Equal(new OpId(3), f.State.LastOpId);
    }

    [Fact]
    public void Applied_SubmitAndServerOperations_RaisedAfterJournalAndNeverForRejected()
    {
        var f = new SimFixture();
        var seen = new List<(OpId Id, int Journaled)>();
        f.Sim.Applied += applied => seen.Add((applied.Id, f.Journal.Entries.Count));
        var p = f.Join();

        f.Sim.Submit(p, f.Dig(p, f.Ground(2)));
        f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { RayDirection = Vector3.UnitY });

        Assert.Equal([new OpId(1), new OpId(2)], seen.Select(s => s.Id));
        Assert.Equal([1, 2], seen.Select(s => s.Journaled));
    }

    [Fact]
    public void Applied_ThrowingSubscriber_DoesNotFaultOrUndoTheOperation()
    {
        var f = new SimFixture();
        f.Sim.Applied += _ => throw new InvalidOperationException("subscriber");
        var p = f.Join();

        var results = Enumerable.Range(0, 5).Select(_ => f.Sim.Submit(p, f.Dig(p, f.Ground(2)))).ToList();

        Assert.All(results, r => Assert.True(r.IsAccepted));
        Assert.False(f.Sim.IsFaulted);
        Assert.Equal(6, f.Journal.Entries.Count);
        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason);
        Assert.Equal(6, f.Journal.Entries.Count);
    }

    [Fact]
    public void Submit_SixthTerrainEditInOneTick_RejectedUntilTheWindowOfOneSecondPasses()
    {
        var f = new SimFixture();
        var p = f.Join();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted);
        }

        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason);
        f.Ticks(11);
        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason);

        // K2-13 W3 (gate decision D13): the bucket alone would accept at tick 12, the sliding window holds the five edits until tick 60.
        f.Ticks(1);
        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason);
        f.Ticks(47);
        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason);
        f.Ticks(1);
        Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted);
    }

    [Fact]
    public void Submit_RemoveLastBlock_RemovesConstruct()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);

        var result = f.Sim.Submit(p, new RemoveBlockIntent(c, new CellPos(0, 0, 0)));

        Assert.True(result.IsAccepted);
        Assert.False(f.State.Constructs.TryGet(c, out _));
        Assert.Equal([new ConstructChange(c, 2, true)], result.Applied!.ConstructChanges);
    }

    [Fact]
    public void LeaveAllOnlinePlayers_AllOffline_InPlayerIdOrder()
    {
        var f = new SimFixture();
        var a = f.Join(10);
        var b = f.Join(20);
        var c = f.Join(30);
        f.Sim.Submit(a, f.Dig(a, f.Ground(2)));
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(b, f.PlayerOf(b).Frame, 0, f.PlayerOf(b).Position, Quaternion.Identity, 0f));
        var before = f.Journal.Entries.Count;

        var applied = f.Sim.LeaveAllOnlinePlayers();

        Assert.Equal([a, c], applied.Select(op => ((PlayerLeaveOp)op.Operation).Player));
        Assert.Equal(before + 2, f.Journal.Entries.Count);
        Assert.All(f.State.Players.All, player => Assert.False(player.IsOnline));
    }

    [Fact]
    public void UpdatePlayerKinematics_InvalidValues_ReturnsFalseAndKeepsState()
    {
        var f = new SimFixture();
        var p = f.Join();
        var player = f.PlayerOf(p);
        var position = player.Position;

        Assert.False(f.Sim.UpdatePlayerKinematics(p, player.Frame, 0, new Vector3(float.NaN, 0, 0), Quaternion.Identity, 0));
        Assert.False(f.Sim.UpdatePlayerKinematics(p, player.Frame, 0, position, new Quaternion(0, 0, 0, 2), 0));
        Assert.False(f.Sim.UpdatePlayerKinematics(p, player.Frame, 0, position, Quaternion.Identity, float.PositiveInfinity));
        Assert.Equal(position, player.Position);
        Assert.Equal(Quaternion.Identity, player.BodyRotation);

        Assert.True(f.Sim.UpdatePlayerKinematics(p, player.Frame, 0, position + Vector3.UnitX, Quaternion.Identity, 0.25f));
        Assert.Equal(position + Vector3.UnitX, player.Position);
        Assert.Equal(0.25f, player.Pitch);
    }

    [Fact]
    public void Submit_TerrainServiceThrows_Faulted()
    {
        var f = new SimFixture();
        var p = f.Join();
        f.Terrain.ThrowOnApply = new IOException("voxel store failed");

        Assert.Throws<IOException>(() => f.Sim.Submit(p, f.Dig(p, f.Ground(2))));

        Assert.True(f.Sim.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => f.Sim.Submit(p, f.Dig(p, f.Ground(2))));
        Assert.Throws<InvalidOperationException>(() => f.Sim.AdvanceTick());
        Assert.Throws<InvalidOperationException>(() => f.Sim.LeaveAllOnlinePlayers());
    }

    [Fact]
    public void ApplyServerOperation_BrokenPrecondition_ThrowsAndChangesNothing()
    {
        var f = new SimFixture();
        f.Join();
        var last = f.State.LastOpId;

        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(new BlockClearOp(new EntityId(77), new CellPos(0, 0, 0))));

        Assert.Equal(last, f.State.LastOpId);
        Assert.False(f.Sim.IsFaulted);
    }

    [Fact]
    public void AdvanceTick_IncrementsCurrentTick()
    {
        var f = new SimFixture();

        f.Ticks(5);

        Assert.Equal(new Tick(5), f.State.CurrentTick);
    }
}
