using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

public sealed class StateCodecContainerTests
{
    private static ContainerRecord Sample()
    {
        var slots = new ItemStack[10];
        slots[0] = new ItemStack(new ItemTypeId(1), 1);
        slots[4] = new ItemStack(new ItemTypeId(300), 500);
        slots[9] = new ItemStack(new ItemTypeId(4096), ushort.MaxValue);
        return new ContainerRecord(
            new ContainerId(70_000), ContainerKind.Chest, ContainerHost.ConstructBlock(new EntityId(5), new CellPos(-1, 2, -3)), new PlayerId(4),
            ContainerAccess.Shared, 10, 123_456, slots);
    }

    [Fact]
    public void ContainerBlob_RoundTrip()
    {
        var record = Sample();

        var blob = StateCodec.EncodeContainerBlob(record);
        var decoded = StateCodec.DecodeContainerBlob(blob);

        Assert.Equal((byte)BlobKind.Container, blob[0]);
        Assert.Equal(20, blob[0]);
        Assert.Equal(1, blob[1]);
        DeepAssert.Equal(record, decoded);
        Assert.Equal(blob, StateCodec.EncodeContainerBlob(decoded));
    }

    [Fact]
    public void ContainerBlob_EmptyContainer_RoundTrip()
    {
        var record = new ContainerRecord(
            new ContainerId(1), ContainerKind.Equipment, ContainerHost.Player(new PlayerId(1)), new PlayerId(1), ContainerAccess.Private, 7, 1,
            new ItemStack[7]);

        DeepAssert.Equal(record, StateCodec.DecodeContainerBlob(StateCodec.EncodeContainerBlob(record)));
    }

    [Fact]
    public void ContainerBlob_WrongKindByte_Throws()
    {
        var blob = StateCodec.EncodeContainerBlob(Sample());
        blob[0] = (byte)BlobKind.Player;

        Kit.AssertViolation(d => StateCodec.DecodeContainerBlob(d), blob);
        Kit.AssertViolation(d => StateCodec.DecodePlayerBlob(d), StateCodec.EncodeContainerBlob(Sample()));
    }

    [Fact]
    public void ContainerBlob_BadSlots_Throw()
    {
        // kind 20, version 1, id 1, Hotbar, host Player 1, owner 1, Private, size 2, version 1, then the slot list.
        const string Head = "14 01 01 02 01 01 01 02 02 01";
        Kit.AssertViolation(d => StateCodec.DecodeContainerBlob(d), Kit.Hex(Head + " 01 02 01 01 00"));
        Kit.AssertViolation(d => StateCodec.DecodeContainerBlob(d), Kit.Hex(Head + " 02 01 01 01 00 00 01 01 00"));
        Kit.AssertViolation(d => StateCodec.DecodeContainerBlob(d), Kit.Hex(Head + " 01 00 00 01 00"));
        Kit.AssertViolation(d => StateCodec.DecodeContainerBlob(d), Kit.Hex(Head + " 01 00 01 00 00"));
        Kit.AssertViolation(d => StateCodec.DecodeContainerBlob(d), Kit.Hex(Head + " 03 00 01 01 00 01 01 01 00 00 01 01 00"));
        Kit.AssertViolation(d => StateCodec.DecodeContainerBlob(d), Kit.Hex("14 01 01 02 01 01 01 02 00 01 00"));
        Assert.NotNull(StateCodec.DecodeContainerBlob(Kit.Hex(Head + " 01 01 01 01 00")));
    }

    [Fact]
    public void ContainerBlob_InvalidRecord_WriteThrows()
    {
        var record = Sample();

        Assert.Throws<ArgumentException>(() => StateCodec.EncodeContainerBlob(record with { Slots = new ItemStack[9] }));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeContainerBlob(record with { Size = 0, Slots = [] }));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeContainerBlob(record with { Slots = [.. record.Slots.Take(9), new ItemStack(new ItemTypeId(1), 0)] }));
    }
}
