using Horyzont.Core.Operations;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>K6-30, contract of operation 49 (<see cref="BlockFunctionStateSetOp"/>).</summary>
public sealed class FunctionStateOpsTests
{
    [Fact]
    public void FunctionStateSet_SetsBits25To27_VersionPlusOne()
    {
        var f = new FunctionalBlockFixture();
        var before = f.BlockAt(FunctionalBlocks.DoorLowCell);
        var version = f.ConstructVersion;

        var applied = f.Apply(SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1));

        var after = f.BlockAt(FunctionalBlocks.DoorLowCell);
        Assert.Equal(1, after.FunctionState);
        Assert.Equal(before.WithFunctionState(1), after);
        Assert.Equal(before.Packed & ~(0x7u << 25), after.Packed & ~(0x7u << 25));
        Assert.Equal(version + 1, f.ConstructVersion);
        Assert.Equal(new ConstructChange(ConstructId, version + 1, false), Assert.Single(applied.ConstructChanges));
        Assert.Equal(0, f.BlockAt(FunctionalBlocks.DoorHighCell).FunctionState);

        f.Apply(SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 7));
        Assert.Equal(7, f.BlockAt(FunctionalBlocks.DoorLowCell).FunctionState);
        Assert.Equal(version + 2, f.ConstructVersion);
    }

    [Fact]
    public void FunctionStateSet_WrongExpectedType_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1));

        f.AssertFails(SetFunctionState(FunctionalBlocks.LampCell, FunctionalBlocks.DoorType, 1));
        f.AssertFails(SetFunctionState(FunctionalBlocks.FreeCell, FunctionalBlocks.DoorType, 1));
        f.AssertFails(SetFunctionState(FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 8));
    }

    [Fact]
    public void FunctionStateSet_UnknownConstruct_Fails()
    {
        var f = new FunctionalBlockFixture();
        f.Apply(SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1));

        f.AssertFails(new BlockFunctionStateSetOp(new Horyzont.Core.Ids.EntityId(99), FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1));
    }
}
