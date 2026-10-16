using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K6-34, format 1 of operation 42 (<see cref="SuitRefillOp"/>, D6). Writing refuses with <see cref="ArgumentException"/>, reading with <c>ProtocolViolationException</c>.</summary>
public sealed class OperationCodecSuitTests
{
    private static readonly PlayerId _player = new(4);

    private static SuitRefillOp Op(SuitResource resource, ushort amount, ushort capacity) => new(_player, resource, amount, capacity);

    [Fact]
    public void SuitRefill_GoldenBytes()
    {
        // 0x2A = 42, format 1, the player, the resource byte (1 to 5), the amount and the capacity (varuint, 1 to 1000).
        Assert.Equal("2A 01 04 05 32 C8 01", Kit.ToHex(Kit.Encode(Op(SuitResource.Oxygen, 50, 200))));
        Assert.Equal("2A 01 04 01 28 64", Kit.ToHex(Kit.Encode(Op(SuitResource.Heating, 40, 100))));

        // The limits: 1 and 1000 (E8 07), the largest player id.
        Assert.Equal("2A 01 04 02 01 01", Kit.ToHex(Kit.Encode(Op(SuitResource.Cooling, 1, 1))));
        Assert.Equal("2A 01 04 04 E8 07 E8 07", Kit.ToHex(Kit.Encode(Op(SuitResource.Shield, 1000, 1000))));
        Assert.Equal(
            "2A 01 FF FF FF FF FF FF FF FF FF 01 03 0A 64",
            Kit.ToHex(Kit.Encode(new SuitRefillOp(new PlayerId(ulong.MaxValue), SuitResource.Filter, 10, 100))));
    }

    [Fact]
    public void SuitRefill_RoundTrip()
    {
        foreach (var resource in Enum.GetValues<SuitResource>())
        {
            RoundTrip(Op(resource, 50, 200));
        }

        RoundTrip(Op(SuitResource.Oxygen, 1, 1));
        RoundTrip(Op(SuitResource.Oxygen, 1000, 1000));
        RoundTrip(new SuitRefillOp(new PlayerId(ulong.MaxValue), SuitResource.Shield, 999, 1));

        // Eating-like use of an item: the destroy with the sink SuitRefill, then the refill (D7).
        RoundTrip(new CompoundOp(
        [
            new ItemDestroyOp(new SlotRef(new ContainerId(1), 3), new ItemTypeId(9), 1, ItemSink.SuitRefill),
            Op(SuitResource.Heating, 40, 100),
        ]));
    }

    [Fact]
    public void SuitRefill_ResourceZeroOrSix_Throws()
    {
        // The accepted counterparts: 1 and 5.
        Assert.IsType<SuitRefillOp>(Kit.DecodeOperation(Kit.Hex("2A 01 04 01 32 64")));
        Assert.IsType<SuitRefillOp>(Kit.DecodeOperation(Kit.Hex("2A 01 04 05 32 64")));

        Kit.AssertOperationViolation("2A 01 04 00 32 64");
        Kit.AssertOperationViolation("2A 01 04 06 32 64");
        Kit.AssertOperationViolation("2A 01 04 FF 32 64");
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op((SuitResource)0, 50, 100)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op((SuitResource)6, 50, 100)));
    }

    [Fact]
    public void SuitRefill_AmountOrCapacityAbove1000_Throws()
    {
        // 1000 = E8 07 is the limit; 1001 = E9 07 is not. Zero is not valid either.
        Assert.IsType<SuitRefillOp>(Kit.DecodeOperation(Kit.Hex("2A 01 04 05 E8 07 E8 07")));

        Kit.AssertOperationViolation("2A 01 04 05 E9 07 64");
        Kit.AssertOperationViolation("2A 01 04 05 32 E9 07");
        Kit.AssertOperationViolation("2A 01 04 05 00 64");
        Kit.AssertOperationViolation("2A 01 04 05 32 00");
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op(SuitResource.Oxygen, 1001, 1000)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op(SuitResource.Oxygen, 10, 1001)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op(SuitResource.Oxygen, 0, 100)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(Op(SuitResource.Oxygen, 10, 0)));
    }

    [Fact]
    public void SuitRefill_TrailingBytes_Throws()
    {
        Assert.IsType<SuitRefillOp>(Kit.DecodeOperation(Kit.Hex("2A 01 04 05 32 C8 01")));

        Kit.AssertOperationViolation("2A 01 04 05 32 C8 01 00");

        // A frame that stops early: after the player, the resource, the amount and inside the capacity.
        Kit.AssertOperationViolation("2A 01 04");
        Kit.AssertOperationViolation("2A 01 04 05");
        Kit.AssertOperationViolation("2A 01 04 05 32");
        Kit.AssertOperationViolation("2A 01 04 05 32 C8");
        Kit.AssertOperationViolation("2A 01");
    }

    [Fact]
    public void SuitRefill_UnknownFormatVersion_Throws()
    {
        Assert.IsType<SuitRefillOp>(Kit.DecodeOperation(Kit.Hex("2A 01 04 05 32 64")));

        Kit.AssertOperationViolation("2A 00 04 05 32 64");
        Kit.AssertOperationViolation("2A 02 04 05 32 64");
    }

    private static void RoundTrip(Operation op)
    {
        var bytes = Kit.Encode(op);
        var decoded = Kit.DecodeOperation(bytes);
        DeepAssert.Equal(op, decoded);
        Assert.Equal(bytes, Kit.Encode(decoded));
    }
}
