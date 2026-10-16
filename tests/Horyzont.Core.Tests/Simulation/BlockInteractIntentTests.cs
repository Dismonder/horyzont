using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// <see cref="BlockInteractIntent"/> (intent 28, K6-30 D6, D7, D10, D18, D21). Tests that expect <see cref="RejectReason.InvalidParameters"/>
/// first accept the matching valid intent: an unknown intent is invalid today, so the refusal alone would prove nothing.
/// </summary>
public sealed class BlockInteractIntentTests
{
    private static BlockInteractIntent Toggle(CellPos cell) => new(ConstructId, cell, BlockInteractAction.Toggle);

    private static BlockInteractIntent Sleep(CellPos cell) => new(ConstructId, cell, BlockInteractAction.Sleep);

    private static BlockInteractIntent SetRespawn(CellPos cell) => new(ConstructId, cell, BlockInteractAction.SetRespawn);

    private static FunctionalBlockFixture Fixture(int players = 1, bool withSurvival = true) => ForIntents(players, withSurvival);

    private static void OpenDoorColumn(FunctionalBlockFixture f, byte state = 1) => f.Apply(new CompoundOp(
    [
        SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, state),
        SetFunctionState(FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, state),
    ]));

    [Fact]
    public void Toggle_ClosedDoor_FunctionStateOpOpens()
    {
        var f = Fixture();
        var lone = new CellPos(4, 1, 0);
        f.Apply(PlaceBlock(lone, FunctionalBlocks.DoorType));
        var version = f.ConstructVersion;

        var applied = f.Accept(Player1, Toggle(lone));

        Assert.Equal(SetFunctionState(lone, FunctionalBlocks.DoorType, 1), applied.Operation);
        Assert.Equal(1, f.BlockAt(lone).FunctionState);
        Assert.Equal(version + 1, f.ConstructVersion);
        Assert.Equal(Player1, applied.Actor);
    }

    [Fact]
    public void Toggle_DoorColumnOfTwo_CompoundOfTwo()
    {
        var f = Fixture();
        var version = f.ConstructVersion;

        var applied = f.Accept(Player1, Toggle(FunctionalBlocks.DoorLowCell));

        var compound = Assert.IsType<CompoundOp>(applied.Operation);
        Assert.Equal(2, compound.Components.Count);
        Assert.Equal(
            [
                SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1),
                SetFunctionState(FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 1),
            ],
            compound.Components.OfType<BlockFunctionStateSetOp>().OrderBy(o => o.Cell.Y).ToArray());
        Assert.Equal(1, f.BlockAt(FunctionalBlocks.DoorLowCell).FunctionState);
        Assert.Equal(1, f.BlockAt(FunctionalBlocks.DoorHighCell).FunctionState);
        Assert.Equal(new ConstructChange(ConstructId, version + 1, false), Assert.Single(applied.ConstructChanges));
    }

    [Fact]
    public void Toggle_DoorColumnOfFour_TogglesThreeFromAimed()
    {
        var f = Fixture();
        f.Apply(PlaceBlock(new CellPos(3, 3, 0), FunctionalBlocks.DoorType));
        f.Apply(PlaceBlock(new CellPos(3, 4, 0), FunctionalBlocks.DoorType));

        // Aimed at the second door from the bottom: one cell down, then one up, three in all (GameRules.MaxDoorColumnCells).
        f.Accept(Player1, Toggle(FunctionalBlocks.DoorHighCell));

        Assert.Equal(1, f.BlockAt(new CellPos(3, 1, 0)).FunctionState);
        Assert.Equal(1, f.BlockAt(new CellPos(3, 2, 0)).FunctionState);
        Assert.Equal(1, f.BlockAt(new CellPos(3, 3, 0)).FunctionState);
        Assert.Equal(0, f.BlockAt(new CellPos(3, 4, 0)).FunctionState);
    }

    [Fact]
    public void Toggle_DoorClosingOnPlayer_IntersectsPlayer()
    {
        var f = Fixture();
        OpenDoorColumn(f);
        f.Move(Player1, new Vector3(3.5f, 4001f, 1.2f));

        // Aimed at the upper door, because the eye stands level with it: the lower one would hide behind the upper one.
        f.AssertRejected(Player1, Toggle(FunctionalBlocks.DoorHighCell), RejectReason.IntersectsPlayer);
        Assert.Equal(1, f.BlockAt(FunctionalBlocks.DoorLowCell).FunctionState);
        Assert.Equal(1, f.BlockAt(FunctionalBlocks.DoorHighCell).FunctionState);
    }

    [Fact]
    public void Toggle_Lamp_SwitchesOff()
    {
        var f = Fixture();
        f.Apply(SetFunctionState(FunctionalBlocks.LampCell, FunctionalBlocks.LampType, 1));

        var applied = f.Accept(Player1, Toggle(FunctionalBlocks.LampCell));

        Assert.Equal(SetFunctionState(FunctionalBlocks.LampCell, FunctionalBlocks.LampType, 0), applied.Operation);
        Assert.Equal(0, f.BlockAt(FunctionalBlocks.LampCell).FunctionState);
    }

    [Fact]
    public void Toggle_PlainBlock_InvalidParameters()
    {
        var f = Fixture();
        f.Accept(Player1, Toggle(FunctionalBlocks.LampCell));

        f.AssertRejected(Player1, Toggle(FunctionalBlocks.FloorCells[0]), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, Toggle(FunctionalBlocks.ChestCell), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Toggle_OutOfReach_OutOfReach()
    {
        var f = Fixture();
        f.Move(Player1, new Vector3(2f, 4001f, 20f));

        f.AssertRejected(Player1, Toggle(FunctionalBlocks.LampCell), RejectReason.OutOfReach);
    }

    [Fact]
    public void Toggle_BehindWall_NoLineOfSight()
    {
        var f = Fixture();
        for (var x = 1; x <= 4; x++)
        {
            f.Apply(PlaceBlock(new CellPos(x, 1, 1), FunctionalBlocks.StoneType));
            f.Apply(PlaceBlock(new CellPos(x, 2, 1), FunctionalBlocks.StoneType));
        }

        f.AssertRejected(Player1, Toggle(FunctionalBlocks.DoorLowCell), RejectReason.NoLineOfSight);
    }

    [Fact]
    public void Toggle_WithoutSurvivalContent_Applies()
    {
        var f = Fixture(withSurvival: false);

        f.Accept(Player1, Toggle(FunctionalBlocks.DoorLowCell));

        Assert.Equal(1, f.BlockAt(FunctionalBlocks.DoorLowCell).FunctionState);
    }

    [Fact]
    public void Sleep_FreeBed_ClaimsAndSetsRespawn()
    {
        var f = Fixture();
        f.SetUpBed(FunctionalBlocks.BedCell);

        f.Accept(Player1, Sleep(FunctionalBlocks.BedCell));

        Assert.True(f.HasState(FunctionalBlocks.BedCell, out var state));
        Assert.Equal(new BedBlockState(Player1), state);
        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Equal(BedAt(FunctionalBlocks.BedCell), progress.RespawnBed);
    }

    [Fact]
    public void Sleep_OwnBedAgain_RespawnOnly()
    {
        var f = Fixture();
        f.SetUpBed(FunctionalBlocks.BedCell, Player1);

        var applied = f.Accept(Player1, Sleep(FunctionalBlocks.BedCell));

        Assert.Equal(
            [new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell))], Flatten(applied.Operation).OfType<RespawnPointSetOp>().ToArray());
        Assert.DoesNotContain(Flatten(applied.Operation), op => op is BlockEntitySetOp);
        Assert.True(f.HasState(FunctionalBlocks.BedCell, out var state));
        Assert.Equal(new BedBlockState(Player1), state);
    }

    [Fact]
    public void Sleep_NewBed_ReleasesOldBed()
    {
        var f = Fixture();
        var second = new CellPos(4, 1, 0);
        f.SetUpBed(FunctionalBlocks.BedCell, Player1);
        f.SetUpBed(second);

        f.Accept(Player1, Sleep(second));

        Assert.True(f.HasState(FunctionalBlocks.BedCell, out var released));
        Assert.Equal(new BedBlockState(default), released);
        Assert.True(f.HasState(second, out var claimed));
        Assert.Equal(new BedBlockState(Player1), claimed);
        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Equal(BedAt(second), progress.RespawnBed);
    }

    [Fact]
    public void SetRespawn_BedOfOther_NotOwner()
    {
        var f = Fixture(players: 2);
        f.SetUpBed(FunctionalBlocks.BedCell, Player2);

        f.AssertRejected(Player1, SetRespawn(FunctionalBlocks.BedCell), RejectReason.NotOwner);
        f.AssertRejected(Player1, Sleep(FunctionalBlocks.BedCell), RejectReason.NotOwner);
        Assert.False(f.State.Progress.TryGet(Player1, out _));
    }

    [Fact]
    public void Sleep_OnDoor_InvalidParameters()
    {
        var f = Fixture();
        f.SetUpBed(FunctionalBlocks.BedCell);
        f.Accept(Player1, Sleep(FunctionalBlocks.BedCell));

        f.AssertRejected(Player1, Sleep(FunctionalBlocks.DoorLowCell), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, SetRespawn(FunctionalBlocks.LampCell), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Sleep_WithoutSurvivalContent_InvalidParameters()
    {
        var f = Fixture(withSurvival: false);
        f.SetUpBed(FunctionalBlocks.BedCell);
        f.Accept(Player1, Toggle(FunctionalBlocks.DoorLowCell));

        f.AssertRejected(Player1, Sleep(FunctionalBlocks.BedCell), RejectReason.InvalidParameters);
        f.AssertRejected(Player1, SetRespawn(FunctionalBlocks.BedCell), RejectReason.InvalidParameters);
    }

    [Fact]
    public void Burst_BlocksBucket_RateLimited()
    {
        var f = Fixture();
        var burst = f.Sim.Rules.BlockEditBurst;

        for (var i = 0; i < burst; i++)
        {
            f.Accept(Player1, Toggle(FunctionalBlocks.LampCell));
        }

        f.AssertRejected(Player1, Toggle(FunctionalBlocks.LampCell), RejectReason.RateLimited);
    }
}
