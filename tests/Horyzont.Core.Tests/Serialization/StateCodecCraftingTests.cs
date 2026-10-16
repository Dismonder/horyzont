using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K6-32: the station state in blob 21, blob 22 in version 2 (discoveries) and the new blob 25 (progress keys).</summary>
public sealed class StateCodecCraftingTests
{
    private static readonly EntityId _construct = new(5);
    private static readonly CellPos _cell = new(1, -1, 0);
    private static readonly BedRef _bed = new(_construct, _cell, 3);

    [Fact]
    public void BlockEntityBlob_Station_RoundTrip()
    {
        foreach (var record in new[]
        {
            new BlockEntityRecord(_construct, _cell, new StationBlockState(new ContainerId(7), new ContainerId(8), string.Empty, null)),
            new BlockEntityRecord(_construct, _cell, new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", null)),
            new BlockEntityRecord(_construct, _cell, new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", new Tick(100))),
            new BlockEntityRecord(_construct, _cell, new StationBlockState(new ContainerId(7), new ContainerId(8), new string('r', 64), new Tick(ulong.MaxValue))),
        })
        {
            var blob = StateCodec.EncodeBlockEntityBlob(record);
            DeepAssert.Equal(record, StateCodec.DecodeBlockEntityBlob(blob));
            Assert.Equal(blob, StateCodec.EncodeBlockEntityBlob(StateCodec.DecodeBlockEntityBlob(blob)));
        }
    }

    [Fact]
    public void StationStartedWithoutRecipe_Blob21_Throws()
    {
        // Review A1 (4-5): the same invariant as in operation 40. The accepted counterpart first.
        Assert.Equal("t:a", Assert.IsType<StationBlockState>(StateCodec.DecodeBlockEntityBlob(Kit.Hex("15 01 05 02 01 00 03 07 08 03 74 3A 61 01 64")).State).Recipe);

        Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), Kit.Hex("15 01 05 02 01 00 03 07 08 00 01 64"));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeBlockEntityBlob(new BlockEntityRecord(_construct, _cell, new StationBlockState(new ContainerId(7), new ContainerId(8), string.Empty, new Tick(100)))));
    }

    [Fact]
    public void PlayerProgressBlob_Discoveries_RoundTrip()
    {
        var five = ItemTypeBitSet.Empty.With(new ItemTypeId(5));
        var many = ItemTypeBitSet.Empty.With(new ItemTypeId(1)).With(new ItemTypeId(9)).With(new ItemTypeId(4095)).With(new ItemTypeId(4096));
        foreach (var record in new[]
        {
            new PlayerProgressRecord(new PlayerId(4), null, five),
            new PlayerProgressRecord(new PlayerId(4), _bed, five),
            new PlayerProgressRecord(new PlayerId(4), _bed, many),
            new PlayerProgressRecord(new PlayerId(4), _bed),
            new PlayerProgressRecord(new PlayerId(4), _bed, ItemTypeBitSet.Empty),
        })
        {
            var blob = StateCodec.EncodePlayerProgressBlob(record);
            Assert.Equal(3, blob[1]);
            DeepAssert.Equal(record, StateCodec.DecodePlayerProgressBlob(blob));
            Assert.Equal(blob, StateCodec.EncodePlayerProgressBlob(StateCodec.DecodePlayerProgressBlob(blob)));
        }
    }

    [Fact]
    public void PlayerProgressBlob_EmptyDiscoveries_NormalizedToNull()
    {
        // The review of A1 (4-4): an empty set is one form, null, so the record with an empty set equals the record without one and has no bit 1.
        var empty = new PlayerProgressRecord(new PlayerId(4), _bed, ItemTypeBitSet.Empty);
        Assert.Null(empty.Discovered);
        Assert.Equal(new PlayerProgressRecord(new PlayerId(4), _bed), empty);

        var blob = StateCodec.EncodePlayerProgressBlob(empty);

        Assert.Equal("16 03 04 01 05 02 01 00 03", Kit.ToHex(blob));
        Assert.Null(StateCodec.DecodePlayerProgressBlob(blob).Discovered);
        Assert.Null(new PlayerProgress(new PlayerId(4), _bed, ItemTypeBitSet.Empty).Discovered);
    }

    [Fact]
    public void Blob22Version1_StillReads()
    {
        // The bytes of K6-30: a respawn point, and no respawn point.
        var withBed = StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 01 04 01 05 02 01 00 03"));
        var without = StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 01 04 00"));

        Assert.Equal(new PlayerId(4), withBed.Player);
        Assert.Equal(_bed, withBed.RespawnBed);
        Assert.Null(withBed.Discovered);
        Assert.Equal(new PlayerId(4), without.Player);
        Assert.Null(without.RespawnBed);
        Assert.Null(without.Discovered);
    }

    [Fact]
    public void Blob22UnknownFlag_Throws()
    {
        // The accepted counterparts first.
        Assert.Equal(new PlayerId(4), StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 02 04 02 01 20")).Player);
        Assert.Equal(new PlayerId(4), StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 02 04 03 05 02 01 00 03 01 20")).Player);

        // Bit 2 (food) is unknown in version 2; version 1 has no bit 1; a version above 3 is unknown (K6-33 writes version 3).
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 02 04 04"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 02 04 06 01 20"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 01 04 02 01 20"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 04 04 00"));
    }

    [Fact]
    public void BitsetTrailingZero_Throws()
    {
        Assert.Equal(new PlayerId(4), StateCodec.DecodePlayerProgressBlob(Kit.Hex("16 02 04 02 02 20 40")).Player);

        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 02 04 02 02 20 00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 02 04 02 01 00"));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 02 04 02 00"));

        // Bit 0 stands for no type.
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 02 04 02 01 21"));

        // A flag with the bitset missing is a short blob.
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), Kit.Hex("16 02 04 02"));
    }

    [Fact]
    public void BitsetOver513_Throws()
    {
        // Item type ids run 1...MaxItemTypes (4096), so bit 4096 sits in byte 512: the longest set is 513 bytes (review A1, 4-2).
        var at512 = Kit.Concat(Kit.Hex("16 02 04 02 80 04"), new byte[511], [0x80]);
        var at513 = Kit.Concat(Kit.Hex("16 02 04 02 81 04"), new byte[512], [0x01]);
        var at514 = Kit.Concat(Kit.Hex("16 02 04 02 82 04"), new byte[513], [0x01]);

        Assert.True(StateCodec.DecodePlayerProgressBlob(at512).Discovered!.Contains(new ItemTypeId(4095)));
        Assert.True(StateCodec.DecodePlayerProgressBlob(at513).Discovered!.Contains(new ItemTypeId(4096)));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), at514);
    }

    [Fact]
    public void BitsetBitAbove4096_Throws()
    {
        // Review B1, 1: the valid twin sets bit 0 of byte 512 (type 4096), the refused blob sets bit 1 (type 4097, which does not exist).
        var valid = Kit.Concat(Kit.Hex("16 02 04 02 81 04"), new byte[512], [0x01]);
        var above = Kit.Concat(Kit.Hex("16 02 04 02 81 04"), new byte[512], [0x02]);

        Assert.True(StateCodec.DecodePlayerProgressBlob(valid).Discovered!.Contains(new ItemTypeId(4096)));
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), above);
    }

    [Fact]
    public void ProgressKeysBlob_RoundTrip()
    {
        // Ordinal order: upper case before lower case.
        foreach (var keys in new[]
        {
            Array.Empty<string>(),
            ["t:boss"],
            ["B", "a"],
            ["\U0001F600", "\uE000"],
            [new string('k', 64)],
            Enumerable.Range(0, 256).Select(i => "k" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
        })
        {
            var blob = StateCodec.EncodeProgressKeysBlob(keys);
            Assert.Equal(keys, StateCodec.DecodeProgressKeysBlob(blob));
            Assert.Equal(blob, StateCodec.EncodeProgressKeysBlob(StateCodec.DecodeProgressKeysBlob(blob)));
        }
    }

    [Fact]
    public void Blob25Unsorted_Throws()
    {
        Assert.Equal(["a", "b"], StateCodec.DecodeProgressKeysBlob(Kit.Hex("19 01 02 01 61 01 62")));

        Kit.AssertViolation(d => StateCodec.DecodeProgressKeysBlob(d), Kit.Hex("19 01 02 01 62 01 61"));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeProgressKeysBlob(["b", "a"]));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeProgressKeysBlob(["a", "B"]));

        // The order is string.CompareOrdinal (UTF-16 units): the surrogate pair (D83D) precedes U+E000, although its UTF-8 bytes would follow them.
        Assert.Equal(["\U0001F600", "\uE000"], StateCodec.DecodeProgressKeysBlob(StateCodec.EncodeProgressKeysBlob(["\U0001F600", "\uE000"])));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeProgressKeysBlob(["\uE000", "\U0001F600"]));
    }

    [Fact]
    public void Blob25Duplicate_Throws()
    {
        Kit.AssertViolation(d => StateCodec.DecodeProgressKeysBlob(d), Kit.Hex("19 01 02 01 61 01 61"));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeProgressKeysBlob(["a", "a"]));
    }

    [Fact]
    public void Blob25Limits_Throw()
    {
        Assert.Single(StateCodec.DecodeProgressKeysBlob(Kit.Concat(Kit.Hex("19 01 01 40"), Enumerable.Repeat((byte)'k', 64).ToArray())));

        // Count above 256, key of 0 and of 65 bytes, a trailing byte, another version.
        Kit.AssertViolation(d => StateCodec.DecodeProgressKeysBlob(d), Kit.Hex("19 01 81 02"));
        Kit.AssertViolation(d => StateCodec.DecodeProgressKeysBlob(d), Kit.Hex("19 01 01 00"));
        Kit.AssertViolation(d => StateCodec.DecodeProgressKeysBlob(d), Kit.Concat(Kit.Hex("19 01 01 41"), Enumerable.Repeat((byte)'k', 65).ToArray()));
        Kit.AssertViolation(d => StateCodec.DecodeProgressKeysBlob(d), Kit.Hex("19 01 00 00"));
        Kit.AssertViolation(d => StateCodec.DecodeProgressKeysBlob(d), Kit.Hex("19 02 00"));
        Kit.AssertViolation(d => StateCodec.DecodeProgressKeysBlob(d), Kit.Hex("16 01 00"));

        Assert.Throws<ArgumentException>(() => StateCodec.EncodeProgressKeysBlob([string.Empty]));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeProgressKeysBlob([new string('k', 65)]));
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeProgressKeysBlob(Enumerable.Range(0, 257).Select(i => "k" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
    }

    [Fact]
    public void GoldenBytes_Blobs()
    {
        Assert.Equal(
            "15 01 05 02 01 00 03 07 08 00 00",
            Kit.ToHex(StateCodec.EncodeBlockEntityBlob(new BlockEntityRecord(_construct, _cell, new StationBlockState(new ContainerId(7), new ContainerId(8), string.Empty, null)))));
        Assert.Equal(
            "15 01 05 02 01 00 03 07 08 03 74 3A 61 01 64",
            Kit.ToHex(StateCodec.EncodeBlockEntityBlob(new BlockEntityRecord(_construct, _cell, new StationBlockState(new ContainerId(7), new ContainerId(8), "t:a", new Tick(100))))));
        Assert.Equal(
            "16 03 04 02 01 20",
            Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(new PlayerId(4), null, ItemTypeBitSet.Empty.With(new ItemTypeId(5))))));
        Assert.Equal(
            "16 03 04 03 05 02 01 00 03 01 20",
            Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(new PlayerId(4), _bed, ItemTypeBitSet.Empty.With(new ItemTypeId(5))))));
        Assert.Equal("16 03 04 01 05 02 01 00 03", Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(new PlayerId(4), _bed))));
        Assert.Equal("19 01 01 06 74 3A 62 6F 73 73", Kit.ToHex(StateCodec.EncodeProgressKeysBlob(["t:boss"])));
        Assert.Equal("19 01 00", Kit.ToHex(StateCodec.EncodeProgressKeysBlob([])));
    }
}
