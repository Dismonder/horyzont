using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>K6-30, contract of operation 44 (<see cref="RespawnPointSetOp"/>).</summary>
public sealed class RespawnPointOpsTests
{
    [Fact]
    public void RespawnPointSet_OnBed_SetsProgress()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));

        f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));

        Assert.Equal(1, f.State.Progress.Count);
        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Equal(new PlayerProgress(Player1, BedAt(FunctionalBlocks.BedCell)), progress);
    }

    [Fact]
    public void RespawnPointSet_CellWithoutBedState_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));

        // A lamp, an empty cell and a bed block without a state are not respawn points.
        f.AssertFails(new RespawnPointSetOp(Player1, new BedRef(ConstructId, FunctionalBlocks.LampCell, FunctionalBlocks.LampType)));
        f.AssertFails(new RespawnPointSetOp(Player1, new BedRef(ConstructId, FunctionalBlocks.FreeCell, FunctionalBlocks.BedType)));
        f.Apply(PlaceBlock(FunctionalBlocks.FreeCell, FunctionalBlocks.BedType));
        f.AssertFails(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.FreeCell)));
        f.AssertFails(new RespawnPointSetOp(Player1, new BedRef(ConstructId, FunctionalBlocks.BedCell, FunctionalBlocks.ChestType)));
    }

    [Fact]
    public void RespawnPointSet_WrongExpectedType_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));

        // The cell holds a bed with a state, but the reference expects a door there.
        f.AssertFails(new RespawnPointSetOp(Player1, new BedRef(ConstructId, FunctionalBlocks.BedCell, FunctionalBlocks.DoorType)));
        Assert.Equal(0, f.State.Progress.Count);
    }

    [Fact]
    public void RespawnPointSet_None_ClearsPoint()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));
        Assert.Equal(1, f.State.Progress.Count);

        f.Apply(new RespawnPointSetOp(Player1, null));

        Assert.Equal(0, f.State.Progress.Count);
        Assert.False(f.State.Progress.TryGet(Player1, out _));
    }

    [Fact]
    public void RespawnPointSet_UnknownPlayer_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));

        f.AssertFails(new RespawnPointSetOp(new PlayerId(99), BedAt(FunctionalBlocks.BedCell)));
        f.AssertFails(new RespawnPointSetOp(new PlayerId(99), null));
    }
}
