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
        // The worst case: 512 stations with the longest recipe key and the largest ids and ticks, and the other 3584 entries chests with the largest ids.
        var limit = new GameRules().MaxTimedStationsPerConstruct;
        Assert.Equal(512, limit);

        var message = Many(limit, ProtocolConstants.MaxBlockEntitiesPerConstruct - limit);
        var frame = Kit.Encode(message);

        Assert.True(frame.Length < ProtocolConstants.MaxReliableMessageBytes, $"{frame.Length} bytes");
        Assert.Equal(message.Entries.Count, Assert.IsType<BlockEntityStates>(MessageCodec.Read(frame)).Entries.Count);
    }

    [Fact]
    public void ConstructWithEveryEntryAStation_ExceedsTheReliableMessage()
    {
        // The reason for the limit: without it 4096 stations would not fit, and the server could not send the construct.
        Assert.Throws<ArgumentException>(() => Kit.Encode(Many(ProtocolConstants.MaxBlockEntitiesPerConstruct, 0)));
    }

    private static BlockEntityStates Many(int stations, int chests)
    {
        var cells = Enumerable.Range(0, stations + chests)
            .Select(i => new CellPos(i % 64, (i / 64) % 64, i / 4096))
            .OrderBy(cell => new BlockRef(new EntityId(1), cell))
            .ToArray();
        var recipe = new string('r', 64);
        var entries = new List<BlockEntityEntry>(cells.Length);
        for (var i = 0; i < cells.Length; i++)
        {
            BlockEntityState state = i < stations
                ? new StationBlockState(new ContainerId(ulong.MaxValue - 1), new ContainerId(ulong.MaxValue), recipe, new Tick(ulong.MaxValue))
                : new ChestBlockState(new ContainerId(ulong.MaxValue));
            entries.Add(new BlockEntityEntry(cells[i], state));
        }

        return new BlockEntityStates(new EntityId(1), entries);
    }
}
