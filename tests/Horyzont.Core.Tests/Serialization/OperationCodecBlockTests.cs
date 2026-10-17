using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K6-30, format 1 of the operations 36, 37, 40, 44 and 49 and of the compound with the new components.</summary>
public sealed class OperationCodecBlockTests
{
    private static readonly EntityId _construct = new(5);
    private static readonly CellPos _cell = new(1, -1, 0);

    [Fact]
    public void RoundTrip_ContainerRemove() => RoundTrip(new ContainerRemoveOp(new ContainerId(300)));

    [Theory]
    [InlineData(ContainerAccess.Shared, 1)]
    [InlineData(ContainerAccess.Private, 256)]
    public void RoundTrip_ContainerConfigure(ContainerAccess access, int size) =>
        RoundTrip(new ContainerConfigureOp(new ContainerId(ulong.MaxValue), (ushort)size, access));

    [Fact]
    public void RoundTrip_BlockEntitySet_ChestBedAndNoState()
    {
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 2, new ChestBlockState(new ContainerId(7))));
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 3, new BedBlockState(new PlayerId(9))));
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 3, new BedBlockState(PlayerId.None)));
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 3, null));
    }

    [Fact]
    public void RoundTrip_RespawnPointSet_WithAndWithoutBed()
    {
        RoundTrip(new RespawnPointSetOp(new PlayerId(4), new BedRef(_construct, _cell, 3)));
        RoundTrip(new RespawnPointSetOp(new PlayerId(4), null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void RoundTrip_BlockFunctionStateSet(int state) => RoundTrip(new BlockFunctionStateSetOp(_construct, _cell, 9, (byte)state));

    [Fact]
    public void GoldenBytes_NewOperations()
    {
        Assert.Equal("24 01 03", Kit.ToHex(Kit.Encode(new ContainerRemoveOp(new ContainerId(3)))));
        Assert.Equal("25 01 03 08 02", Kit.ToHex(Kit.Encode(new ContainerConfigureOp(new ContainerId(3), 8, ContainerAccess.Private))));
        Assert.Equal("28 01 05 02 01 00 02 01 07", Kit.ToHex(Kit.Encode(new BlockEntitySetOp(_construct, _cell, 2, new ChestBlockState(new ContainerId(7))))));
        Assert.Equal("28 01 05 02 01 00 03 02 09", Kit.ToHex(Kit.Encode(new BlockEntitySetOp(_construct, _cell, 3, new BedBlockState(new PlayerId(9))))));
        Assert.Equal("28 01 05 02 01 00 03 00", Kit.ToHex(Kit.Encode(new BlockEntitySetOp(_construct, _cell, 3, null))));
        Assert.Equal("2C 01 04 01 05 02 01 00 03", Kit.ToHex(Kit.Encode(new RespawnPointSetOp(new PlayerId(4), new BedRef(_construct, _cell, 3)))));
        Assert.Equal("2C 01 04 00", Kit.ToHex(Kit.Encode(new RespawnPointSetOp(new PlayerId(4), null))));
        Assert.Equal("31 01 05 02 01 00 09 01", Kit.ToHex(Kit.Encode(new BlockFunctionStateSetOp(_construct, _cell, 9, 1))));
    }

    [Fact]
    public void ReservedBlockEntityKind_Read_Throws()
    {
        // The frame without a state ends with the kind byte 0. The reserved kinds 3..7 and anything above are refused, 1 and 2 need their fields.
        var frame = Kit.Encode(new BlockEntitySetOp(_construct, _cell, 3, null));
        Assert.IsType<BlockEntitySetOp>(Kit.DecodeOperation(frame));

        foreach (var kind in new byte[] { 3, 4, 5, 6, 7, 8, 255 })
        {
            var reserved = (byte[])frame.Clone();
            reserved[^1] = kind;
            Kit.AssertViolation(d => Kit.DecodeOperation(d), reserved);
        }
    }

    [Fact]
    public void FunctionStateAbove7_Write_Throws()
    {
        RoundTrip(new BlockFunctionStateSetOp(_construct, _cell, 9, 7));

        Assert.Throws<ArgumentException>(() => Kit.Encode(new BlockFunctionStateSetOp(_construct, _cell, 9, 8)));
        var frame = Kit.Encode(new BlockFunctionStateSetOp(_construct, _cell, 9, 7));
        frame[^1] = 8;
        Kit.AssertViolation(d => Kit.DecodeOperation(d), frame);
    }

    [Fact]
    public void CompoundWithNewComponents_RoundTrip()
    {
        var chest = new ContainerCreateOp(
            new ContainerId(4), ContainerKind.Chest, ContainerHost.ConstructBlock(_construct, _cell), new PlayerId(1), ContainerAccess.Shared, 8);
        var state = new BlockEntitySetOp(_construct, _cell, 2, new ChestBlockState(new ContainerId(4)));
        var block = new BlockSetOp(_construct, _cell, BlockState.Create(2, 0));

        RoundTrip(new CompoundOp([chest, state, block]));
        RoundTrip(new CompoundOp([new ContainerRemoveOp(new ContainerId(4)), new ContainerConfigureOp(new ContainerId(5), 4, ContainerAccess.Private), new RespawnPointSetOp(new PlayerId(1), null)]));
        RoundTrip(new CompoundOp(
        [
            new BlockFunctionStateSetOp(_construct, _cell, 9, 1),
            new BlockFunctionStateSetOp(_construct, new CellPos(1, 0, 0), 9, 1),
        ]));
    }

    private static void RoundTrip(Operation op)
    {
        var bytes = Kit.Encode(op);
        var decoded = Kit.DecodeOperation(bytes);
        DeepAssert.Equal(op, decoded);
        Assert.Equal(bytes, Kit.Encode(decoded));
    }
}
