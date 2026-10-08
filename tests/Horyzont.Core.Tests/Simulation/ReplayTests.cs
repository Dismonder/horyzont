using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Simulation;

public sealed class ReplayTests
{
    internal static SimFixture PlayScenario()
    {
        var f = new SimFixture();
        var a = f.Join(1, "alpha");
        var b = f.Join(2, "beta");
        f.Ticks(2);
        f.Sim.Submit(a, f.Dig(a, f.Ground(-2)));
        var c = f.BuildOnTerrain(a, 2);
        f.Ticks(7);
        f.Sim.Submit(a, new PlaceBlockIntent(new ConstructFaceTarget(c, new CellPos(0, 0, 0), Face.PosY), f.Stone, 5));
        f.Sim.Submit(b, f.Fill(b, f.Ground(0, -4)));
        f.Ticks(9);
        f.Sim.Submit(a, new RemoveBlockIntent(c, new CellPos(0, 1, 0)));
        f.BuildOnTerrain(b, 0, 3);
        f.Ticks(4);
        f.Sim.LeaveAllOnlinePlayers();
        f.Join(1, "alpha again");
        return f;
    }

    [Fact]
    public void Replay_WholeJournal_SameHashAndTick()
    {
        var live = PlayScenario();
        var fresh = new SimFixture();

        foreach (var entry in live.Journal.Entries)
        {
            fresh.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(live.State), StateHasher.Hash(fresh.State));
        Assert.Equal(live.Journal.Entries[^1].Tick, fresh.State.CurrentTick);
        Assert.Equal(live.State.LastOpId, fresh.State.LastOpId);
        Assert.Empty(fresh.Journal.Entries);
        Assert.Equal(live.Terrain.AppliedEdits, fresh.Terrain.AppliedEdits);
        Assert.Equal(fresh.Terrain.AppliedEdits.Count, fresh.Terrain.BlockingLoads.Count);
    }

    [Fact]
    public void Replay_GapInOpIds_ThrowsWithNumber()
    {
        var live = PlayScenario();
        var fresh = new SimFixture();

        var exception = Assert.Throws<ReplayFailedException>(() => fresh.Sim.Replay(live.Journal.Entries[1]));

        Assert.Equal(new OpId(2), exception.OpId);
    }

    [Fact]
    public void Replay_BrokenPrecondition_ThrowsWithNumberAndKind()
    {
        var fresh = new SimFixture();
        var entry = new JournalEntry(new OpId(1), new Tick(0), null, new BlockClearOp(new EntityId(5), new CellPos(0, 0, 0)));

        var exception = Assert.Throws<ReplayFailedException>(() => fresh.Sim.Replay(entry));

        Assert.Equal(new OpId(1), exception.OpId);
        Assert.Equal(OperationKind.BlockClear, exception.Kind);
        Assert.Equal(OpId.None, fresh.State.LastOpId);
        Assert.False(fresh.Sim.IsFaulted);
    }
}
