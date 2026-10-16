using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K6-32, format 1 of the operations 43 and 45 and of the station state in operation 40. Writing refuses with <see cref="ArgumentException"/>, reading with <c>ProtocolViolationException</c>.</summary>
public sealed class OperationCodecCraftingTests
{
    private static readonly EntityId _construct = new(5);
    private static readonly CellPos _cell = new(1, -1, 0);

    [Fact]
    public void RoundTrip_ItemDiscoverAndProgressKeyGrant()
    {
        RoundTrip(new ItemDiscoverOp(new PlayerId(4), new ItemTypeId(5)));
        RoundTrip(new ItemDiscoverOp(new PlayerId(ulong.MaxValue), new ItemTypeId(4095)));
        RoundTrip(new ProgressKeyGrantOp("t:boss"));
        RoundTrip(new ProgressKeyGrantOp("k"));
        RoundTrip(new ProgressKeyGrantOp(new string('k', 64)));
        RoundTrip(new ProgressKeyGrantOp(new string('ą', 32)));
    }

    [Fact]
    public void RoundTrip_BlockEntitySet_Station()
    {
        // With and without a recipe, running and idle, and the longest recipe key.
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 11, new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", new Tick(100))));
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 11, new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", null)));
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 11, new StationBlockState(new ContainerId(7), new ContainerId(8), string.Empty, null)));
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 11, new StationBlockState(new ContainerId(ulong.MaxValue - 1), new ContainerId(ulong.MaxValue), new string('r', 64), new Tick(ulong.MaxValue))));
    }

    [Fact]
    public void CompoundWithProgressComponents_RoundTrip()
    {
        RoundTrip(new CompoundOp(
        [
            new ItemDestroyOp(new SlotRef(new ContainerId(4), 0), new ItemTypeId(3), 2, ItemSink.Craft),
            new ItemCreateOp(new SlotRef(new ContainerId(1), 0), new ItemTypeId(2), 1, 5, ItemSource.Craft),
            new ItemDiscoverOp(new PlayerId(1), new ItemTypeId(2)),
            new ProgressKeyGrantOp("t:boss"),
        ]));
    }

    [Fact]
    public void GoldenBytes_CraftingOperations()
    {
        Assert.Equal("2B 01 04 05", Kit.ToHex(Kit.Encode(new ItemDiscoverOp(new PlayerId(4), new ItemTypeId(5)))));
        Assert.Equal("2D 01 06 74 3A 62 6F 73 73", Kit.ToHex(Kit.Encode(new ProgressKeyGrantOp("t:boss"))));
        Assert.Equal(
            "28 01 05 02 01 00 0B 03 07 08 03 74 3A 61 01 64",
            Kit.ToHex(Kit.Encode(new BlockEntitySetOp(_construct, _cell, 11, new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", new Tick(100))))));
        Assert.Equal(
            "28 01 05 02 01 00 0B 03 07 08 00 00",
            Kit.ToHex(Kit.Encode(new BlockEntitySetOp(_construct, _cell, 11, new StationBlockState(new ContainerId(7), new ContainerId(8), string.Empty, null)))));
    }

    [Fact]
    public void StationFlagsUnknownBit_Read_Throws()
    {
        var running = Kit.Encode(new BlockEntitySetOp(_construct, _cell, 11, new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", new Tick(100))));
        var idle = Kit.Encode(new BlockEntitySetOp(_construct, _cell, 11, new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", null)));
        Assert.IsType<BlockEntitySetOp>(Kit.DecodeOperation(running));
        Assert.IsType<BlockEntitySetOp>(Kit.DecodeOperation(idle));

        // Bit 1 is unknown; bit 0 clear with a tick after it leaves a trailing byte; bit 0 set without a tick is a short frame.
        var unknown = (byte[])running.Clone();
        unknown[^2] = 3;
        var clearWithTick = (byte[])running.Clone();
        clearWithTick[^2] = 0;
        var unknownIdle = (byte[])idle.Clone();
        unknownIdle[^1] = 2;
        var setWithoutTick = (byte[])idle.Clone();
        setWithoutTick[^1] = 1;
        foreach (var damaged in new[] { unknown, clearWithTick, unknownIdle, setWithoutTick })
        {
            Kit.AssertViolation(d => Kit.DecodeOperation(d), damaged);
        }
    }

    [Fact]
    public void StationRecipeOver64Bytes_Throws()
    {
        var station = new StationBlockState(new ContainerId(7), new ContainerId(8), new string('r', 64), null);
        RoundTrip(new BlockEntitySetOp(_construct, _cell, 11, station));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new BlockEntitySetOp(_construct, _cell, 11, station with { Recipe = new string('r', 65) })));

        // A frame that declares 65 bytes of recipe.
        var frame = Kit.Concat(Kit.Hex("28 01 05 02 01 00 0B 03 07 08 41"), new byte[65], [0]);
        Kit.AssertViolation(d => Kit.DecodeOperation(d), frame);
    }

    [Fact]
    public void DiscoverTypeZero_Write_Throws()
    {
        RoundTrip(new ItemDiscoverOp(new PlayerId(4), new ItemTypeId(1)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(new ItemDiscoverOp(new PlayerId(4), ItemTypeId.None)));
        Kit.AssertOperationViolation("2B 01 04 00");
    }

    [Fact]
    public void GrantKeyOver64Bytes_Write_Throws()
    {
        RoundTrip(new ProgressKeyGrantOp(new string('k', 64)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(new ProgressKeyGrantOp(new string('k', 65))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ProgressKeyGrantOp(new string('ą', 33))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ProgressKeyGrantOp(string.Empty)));

        Kit.AssertOperationViolation("2D 01 00");
        Kit.AssertViolation(d => Kit.DecodeOperation(d), Kit.Concat(Kit.Hex("2D 01 41"), new byte[65]));
    }

    private static void RoundTrip(Operation op)
    {
        var bytes = Kit.Encode(op);
        var decoded = Kit.DecodeOperation(bytes);
        DeepAssert.Equal(op, decoded);
        Assert.Equal(bytes, Kit.Encode(decoded));
    }
}
