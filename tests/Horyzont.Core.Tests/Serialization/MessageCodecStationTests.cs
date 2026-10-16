using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Station states in message 46 (<see cref="BlockEntityStates"/>, K6-30 D15, K6-32 D6, review A1 note a): the kind byte 3 carries the same state as blob 21,
/// and a construct with the most stations the rules allow still fits one reliable message.
/// </summary>
public sealed class MessageCodecStationTests
{
    private static readonly CellPos _cell = new(1, -1, 0);

    private static BlockEntityStates One(StationBlockState state) => new(new EntityId(5), [new BlockEntityEntry(_cell, state)]);

    [Fact]
    public void StationEntries_RoundTrip()
    {
        BlockEntityStates[] messages =
        [
            One(new StationBlockState(new ContainerId(7), new ContainerId(8), string.Empty, null)),
            One(new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", new Tick(100))),
            One(new StationBlockState(new ContainerId(ulong.MaxValue - 1), new ContainerId(ulong.MaxValue), new string('r', 64), new Tick(0))),
        ];

        foreach (var message in messages)
        {
            var read = Assert.IsType<BlockEntityStates>(MessageCodec.Read(Kit.Encode(message)));
            Assert.Equal(message.Construct, read.Construct);
            Assert.Equal(message.Entries, read.Entries);
        }
    }

    [Fact]
    public void StationEntries_GoldenBytes()
    {
        Assert.Equal(
            "2E 01 05 01 02 01 00 03 07 08 00 00",
            Kit.ToHex(Kit.Encode(One(new StationBlockState(new ContainerId(7), new ContainerId(8), string.Empty, null)))));
        Assert.Equal(
            "2E 01 05 01 02 01 00 03 07 08 03 74 3A 61 01 64",
            Kit.ToHex(Kit.Encode(One(new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", new Tick(100))))));
    }

    [Fact]
    public void Read_StationStateWithStartButNoRecipe_Throws()
    {
        // Control: with a recipe the start is accepted; flag bit 0 is the start, other bits are refused.
        var read = Assert.IsType<BlockEntityStates>(MessageCodec.Read(Kit.Hex("2E 01 05 01 02 01 00 03 07 08 03 74 3A 61 01 64")));
        Assert.Single(read.Entries);

        Kit.AssertMessageViolation("2E 01 05 01 02 01 00 03 07 08 00 01 64");
        Kit.AssertMessageViolation("2E 01 05 01 02 01 00 03 07 08 03 74 3A 61 02 64");
    }

    [Fact]
    public void ConstructWithTheMostTimedStations_FitsOneReliableMessage()
    {
        // The real worst case (review B1, 4 and 6): the construct id ulong.MaxValue (10 bytes), cells with 2-byte zigzag coordinates (6 bytes), 512 stations
        // with the longest recipe key and the largest ids and ticks (103 bytes each) and 3584 chests with the largest ids (17 bytes each):
        // 2 + 10 + 2 + 512 x 103 + 3584 x 17 = 113 678 bytes.
        var limit = new GameRules().MaxTimedStationsPerConstruct;
        Assert.Equal(ProtocolConstants.MaxTimedStationsPerConstruct, limit);
        Assert.Equal(512, limit);

        var message = MaxMessages.BlockEntityStatesOf(limit, ProtocolConstants.MaxBlockEntitiesPerConstruct - limit);
        var frame = Kit.Encode(message);

        Assert.Equal(113_678, frame.Length);
        Assert.True(frame.Length < ProtocolConstants.MaxReliableMessageBytes, $"{frame.Length} bytes");
        Assert.Equal(message.Entries.Count, Assert.IsType<BlockEntityStates>(MessageCodec.Read(frame)).Entries.Count);
    }

    [Fact]
    public void ConstructWithEveryEntryAStation_ExceedsTheReliableMessage()
    {
        // The reason for the limit: without it 4096 stations would not fit, and the server could not send the construct.
        Assert.Throws<ArgumentException>(() => Kit.Encode(MaxMessages.BlockEntityStatesOf(ProtocolConstants.MaxBlockEntitiesPerConstruct, 0)));
    }
}
