using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Message 46 (<see cref="BlockEntityStates"/>) and the new reject reasons in <see cref="IntentReply"/> (K6-30 D15), format 1.
/// </summary>
public sealed class MessageCodecBlockEntityTests
{
    private const MessageType BlockEntityStatesType = MessageType.BlockEntityStates;

    private static readonly CellPos _cell = new(1, -1, 0);

    private static BlockEntityStates Chest() => new(new EntityId(5), [new BlockEntityEntry(_cell, new ChestBlockState(new ContainerId(7)))]);

    public static TheoryData<Message> Messages() =>
    [
        new BlockEntityStates(new EntityId(1), []),
        Chest(),
        new BlockEntityStates(
            new EntityId(ulong.MaxValue),
            [
                new BlockEntityEntry(new CellPos(-2048, 2047, -1), new ChestBlockState(new ContainerId(ulong.MaxValue))),
                new BlockEntityEntry(new CellPos(0, 0, 0), new BedBlockState(new PlayerId(3))),
                new BlockEntityEntry(new CellPos(2047, -2048, 9), new BedBlockState(PlayerId.None)),
            ]),
        new IntentSubmit(9, new QuickStackIntent()),
        new IntentSubmit(10, new BlockInteractIntent(new EntityId(5), _cell, BlockInteractAction.Toggle)),
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
    public void BlockEntityStates_Type_IsFortySix()
    {
        Assert.Equal(BlockEntityStatesType, Chest().Type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(BlockEntityStatesType));
    }

    [Fact]
    public void GoldenBytes()
    {
        Assert.Equal("2E 01 05 00", Kit.ToHex(Kit.Encode(new BlockEntityStates(new EntityId(5), []))));
        Assert.Equal("2E 01 05 01 02 01 00 01 07", Kit.ToHex(Kit.Encode(Chest())));
        Assert.Equal(
            "2E 01 05 02 02 01 00 02 09 00 00 00 02 00",
            Kit.ToHex(Kit.Encode(new BlockEntityStates(
                new EntityId(5),
                [new BlockEntityEntry(_cell, new BedBlockState(new PlayerId(9))), new BlockEntityEntry(default, new BedBlockState(PlayerId.None))]))));
    }

    [Fact]
    public void Write_4097Entries_Throws()
    {
        static BlockEntityStates Many(int count) => new(
            new EntityId(1),
            [.. Enumerable.Range(0, count)
                .Select(i => new CellPos(i % 64, (i / 64) % 64, i / 4096))
                .OrderBy(cell => new BlockRef(new EntityId(1), cell))
                .Select(cell => new BlockEntityEntry(cell, new BedBlockState(PlayerId.None)))]);

        var full = Many(ProtocolConstants.MaxBlockEntitiesPerConstruct);
        DeepAssert.Equal(full, MessageCodec.Read(Kit.Encode(full)));
        Assert.True(Kit.Encode(full).Length < ProtocolConstants.MaxReliableMessageBytes);

        Assert.Throws<ArgumentException>(() => Kit.Encode(Many(ProtocolConstants.MaxBlockEntitiesPerConstruct + 1)));
    }

    [Fact]
    public void Read_Invalid_Throws()
    {
        DeepAssert.Equal(Chest(), MessageCodec.Read(Kit.Hex("2E 01 05 01 02 01 00 01 07")));

        // Count 4097, unknown and reserved kinds (0, 3..7, 8), a version from the future, a missing field, trailing bytes.
        Kit.AssertMessageViolation("2E 01 05 81 20");
        Kit.AssertMessageViolation("2E 01 05 01 02 01 00 00 07");
        Kit.AssertMessageViolation("2E 01 05 01 02 01 00 03 07");
        Kit.AssertMessageViolation("2E 01 05 01 02 01 00 07 07");
        Kit.AssertMessageViolation("2E 01 05 01 02 01 00 08 07");
        Kit.AssertMessageViolation("2E 02 05 00");
        Kit.AssertMessageViolation("2E 01 05 01 02 01 00 01");
        Kit.AssertMessageViolation("2E 01 05 00 00");
    }

    [Fact]
    public void Write_UnsortedOrDuplicateEntries_Throws()
    {
        var first = new BlockEntityEntry(default, new BedBlockState(PlayerId.None));
        var second = new BlockEntityEntry(_cell, new BedBlockState(PlayerId.None));

        // Control: the entries in strictly ascending order (chunk, then index in the chunk) are written.
        DeepAssert.Equal(new BlockEntityStates(new EntityId(5), [second, first]), MessageCodec.Read(Kit.Encode(new BlockEntityStates(new EntityId(5), [second, first]))));

        Assert.Throws<ArgumentException>(() => Kit.Encode(new BlockEntityStates(new EntityId(5), [first, second])));
        Assert.Throws<ArgumentException>(() => Kit.Encode(new BlockEntityStates(new EntityId(5), [first, first])));
    }

    [Fact]
    public void Read_UnsortedOrDuplicateEntries_Throws()
    {
        // Control: (1,-1,0) is in chunk (0,-1,0), which precedes (0,0,0) in chunk (0,0,0).
        DeepAssert.Equal(
            new BlockEntityStates(
                new EntityId(5),
                [new BlockEntityEntry(_cell, new BedBlockState(PlayerId.None)), new BlockEntityEntry(default, new BedBlockState(PlayerId.None))]),
            MessageCodec.Read(Kit.Hex("2E 01 05 02 02 01 00 02 00 00 00 00 02 00")));

        Kit.AssertMessageViolation("2E 01 05 02 00 00 00 02 00 02 01 00 02 00");
        Kit.AssertMessageViolation("2E 01 05 02 00 00 00 02 00 00 00 00 02 00");
        Kit.AssertMessageViolation("2E 01 05 02 02 01 00 02 00 02 01 00 02 01");
    }

    [Fact]
    public void Read_ReservedOrEmptyKind_Throws()
    {
        // Control: kinds 1 and 2 are accepted.
        DeepAssert.Equal(Chest(), MessageCodec.Read(Kit.Hex("2E 01 05 01 02 01 00 01 07")));

        // Kind 0 is an empty state (not an entry), 3..7 are reserved for later packages, 8 and 255 are unknown.
        foreach (var kind in new[] { "00", "03", "04", "05", "06", "07", "08", "FF" })
        {
            Kit.AssertMessageViolation($"2E 01 05 01 02 01 00 {kind} 07");
        }
    }

    [Fact]
    public void IntentReply_FunctionalBlockReasons_RoundTrip()
    {
        foreach (var reason in new[] { RejectReason.NotOwner, RejectReason.ContainerNotEmpty })
        {
            var reply = new IntentReply(3, reason, OpId.None);

            Assert.Equal(reply, MessageCodec.Read(Kit.Encode(reply)));
        }

        Kit.AssertMessageViolation("0C 01 03 37 00");
        Kit.AssertMessageViolation("0C 01 03 38 00");
        Kit.AssertMessageViolation("0C 01 03 3A 00");
    }
}
