using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Messages 40-43 and 49 (K6-29W), format 1.</summary>
public sealed class MessageCodecContainerTests
{
    private static SlotContent Slot(int slot, int type, int count) => new((ushort)slot, new ItemStack(new ItemTypeId((ushort)type), (ushort)count));

    private static ContainerState State(params SlotContent[] slots) =>
        new(new ContainerId(70_000), RejectReason.None, ContainerKind.Hotbar, ContainerAccess.Private, 10, 123_456, slots);

    public static TheoryData<Message> Messages() =>
    [
        State(Slot(0, 1, 1), Slot(4, 4096, 500), Slot(9, 300, ushort.MaxValue)),
        State(),
        ContainerState.Rejected(new ContainerId(5), RejectReason.ContainerLocked),
        new ContainerDelta(new ContainerId(1), 2, [Slot(0, 3, 6), new SlotContent(7, ItemStack.Empty), Slot(255, 1, 1)]),
        new ContainerDelta(new ContainerId(ulong.MaxValue), uint.MaxValue, []),
        new ContainerSubscribe(new ContainerId(1)),
        new ContainerSubscribe(new ContainerId(ulong.MaxValue)),
        new ContainerUnsubscribe(new ContainerId(42)),
        new ItemRegistry([new RegistryEntry(1, "core:multitool"), new RegistryEntry(4096, "core:ore")]),
        new ItemRegistry([]),
    ];

    [Theory]
    [MemberData(nameof(Messages))]
    public void RoundTrip(Message message)
    {
        var frame = Kit.Encode(message);

        DeepAssert.Equal(message, MessageCodec.Read(frame));
        Assert.Equal((byte)message.Type, frame[0]);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(message.Type));
    }

    [Fact]
    public void GoldenBytes()
    {
        Assert.Equal("28 01 05 2B", Kit.ToHex(Kit.Encode(ContainerState.Rejected(new ContainerId(5), RejectReason.ContainerLocked))));
        Assert.Equal(
            "28 01 05 00 02 02 0A 03 02 00 01 01 00 07 00",
            Kit.ToHex(Kit.Encode(new ContainerState(new ContainerId(5), RejectReason.None, ContainerKind.Hotbar, ContainerAccess.Private, 10, 3,
                [Slot(0, 1, 1), new SlotContent(7, ItemStack.Empty)]))));
        Assert.Equal("29 01 01 02 01 03 03 06 00", Kit.ToHex(Kit.Encode(new ContainerDelta(new ContainerId(1), 2, [Slot(3, 3, 6)]))));
        Assert.Equal("2A 01 07", Kit.ToHex(Kit.Encode(new ContainerSubscribe(new ContainerId(7)))));
        Assert.Equal("2B 01 07", Kit.ToHex(Kit.Encode(new ContainerUnsubscribe(new ContainerId(7)))));
        Assert.Equal("31 01 01 01 01 61", Kit.ToHex(Kit.Encode(new ItemRegistry([new RegistryEntry(1, "a")]))));
    }

    [Fact]
    public void Write_257Slots_Throws()
    {
        var slots = Enumerable.Range(0, 257).Select(i => Slot(i, 1, 1)).ToArray();

        Assert.Throws<ArgumentException>(() => Kit.Encode(new ContainerDelta(new ContainerId(1), 1, slots)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(
            new ContainerState(new ContainerId(1), RejectReason.None, ContainerKind.Chest, ContainerAccess.Shared, 256, 1, slots)));
    }

    [Fact]
    public void Write_InvalidContent_Throws()
    {
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(Slot(10, 1, 1))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(Slot(2, 1, 1), Slot(2, 1, 1))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(Slot(3, 1, 1), Slot(2, 1, 1))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State(Slot(3, 1, 0))));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State() with { Size = 0 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State() with { Size = 257 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State() with { Kind = (ContainerKind)10 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(State() with { Access = 0 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(ContainerState.Rejected(new ContainerId(1), RejectReason.ContainerLocked) with { Size = 3 }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(ContainerState.Rejected(new ContainerId(1), (RejectReason)44)));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ContainerDelta(new ContainerId(1), 1, [Slot(256, 1, 1)])));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new ItemRegistry([new RegistryEntry(4097, "a")])));
    }

    [Theory]
    [InlineData("28 01 05 2C")]
    [InlineData("28 01 05 16")]
    [InlineData("28 01 05 00 0A 02 0A 03 00")]
    [InlineData("28 01 05 00 02 03 0A 03 00")]
    [InlineData("28 01 05 00 02 02 00 03 00")]
    [InlineData("28 01 05 00 02 02 81 02 03 00")]
    [InlineData("28 01 05 00 02 02 0A 03 01 0A 01 01 00")]
    [InlineData("28 01 05 00 02 02 0A 03 02 01 00 01 00")]
    [InlineData("28 01 05 00 02 02 0A 03 01 00 01 01 01")]
    [InlineData("28 01 05 00 02 02 0A 03 01 00 01 00 00")]
    [InlineData("28 01 05 00 02 02 0A 03 0B")]
    [InlineData("29 01 01 02 01 80 02 01 01 00")]
    [InlineData("29 01 01 02 81 02")]
    [InlineData("31 01 01 81 20 01 61")]
    [InlineData("2C 01 01")]
    [InlineData("30 01 01")]
    [InlineData("2A 01 07 00")]
    public void Read_Invalid_Throws(string hex) => Kit.AssertMessageViolation(hex);

    [Fact]
    public void IntentReply_SurvivalReasons_RoundTrip()
    {
        foreach (var reason in new[] { RejectReason.InventoryFull, RejectReason.NotEnoughItems, RejectReason.ContainerChanged, RejectReason.ContainerLocked })
        {
            var reply = new IntentReply(3, reason, OpId.None);
            Assert.Equal(reply, MessageCodec.Read(Kit.Encode(reply)));
        }

        Kit.AssertMessageViolation("0C 01 03 16 00");
        Kit.AssertMessageViolation("0C 01 03 2C 00");
        Kit.AssertMessageViolation("0C 01 03 26 00");
    }
}
