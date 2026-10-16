using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>K6-30, blobs 21 (<c>BlockEntity</c>) and 22 (<c>PlayerProgress</c>), version 1.</summary>
public sealed class StateCodecBlockEntityTests
{
    private static readonly EntityId _construct = new(5);
    private static readonly CellPos _cell = new(1, -1, 0);

    [Fact]
    public void BlockEntityBlob_RoundTrip_BothKinds()
    {
        foreach (var record in new[]
        {
            new BlockEntityRecord(_construct, _cell, new ChestBlockState(new ContainerId(7))),
            new BlockEntityRecord(_construct, _cell, new BedBlockState(new PlayerId(9))),
            new BlockEntityRecord(new EntityId(1_000_000), new CellPos(-2048, 2047, 0), new BedBlockState(PlayerId.None)),
        })
        {
            var blob = StateCodec.EncodeBlockEntityBlob(record);
            DeepAssert.Equal(record, StateCodec.DecodeBlockEntityBlob(blob));
            Assert.Equal(blob, StateCodec.EncodeBlockEntityBlob(StateCodec.DecodeBlockEntityBlob(blob)));
        }
    }

    [Fact]
    public void BlockEntityBlob_GoldenBytes()
    {
        Assert.Equal(
            "15 01 05 02 01 00 01 07",
            Kit.ToHex(StateCodec.EncodeBlockEntityBlob(new BlockEntityRecord(_construct, _cell, new ChestBlockState(new ContainerId(7))))));
        Assert.Equal(
            "15 01 05 02 01 00 02 09",
            Kit.ToHex(StateCodec.EncodeBlockEntityBlob(new BlockEntityRecord(_construct, _cell, new BedBlockState(new PlayerId(9))))));
    }

    [Fact]
    public void PlayerProgressBlob_RoundTrip_WritesVersion2()
    {
        foreach (var record in new[]
        {
            new PlayerProgressRecord(new PlayerId(4), new BedRef(_construct, _cell, 3)),
            new PlayerProgressRecord(new PlayerId(4), null),
        })
        {
            var blob = StateCodec.EncodePlayerProgressBlob(record);
            DeepAssert.Equal(record, StateCodec.DecodePlayerProgressBlob(blob));
        }

        Assert.Equal(
            "16 02 04 01 05 02 01 00 03",
            Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(new PlayerId(4), new BedRef(_construct, _cell, 3)))));
        Assert.Equal("16 02 04 00", Kit.ToHex(StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(new PlayerId(4), null))));
    }

    [Fact]
    public void UnknownBlobVersionOrKind_Throws()
    {
        var entity = StateCodec.EncodeBlockEntityBlob(new BlockEntityRecord(_construct, _cell, new BedBlockState(new PlayerId(9))));
        var progress = StateCodec.EncodePlayerProgressBlob(new PlayerProgressRecord(new PlayerId(4), null));
        Assert.Equal(_cell, StateCodec.DecodeBlockEntityBlob(entity).Cell);
        Assert.Equal(new PlayerId(4), StateCodec.DecodePlayerProgressBlob(progress).Player);

        var futureEntity = (byte[])entity.Clone();
        futureEntity[1] = 2;
        Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), futureEntity);
        var futureProgress = (byte[])progress.Clone();
        futureProgress[1] = 3;
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), futureProgress);

        // A blob of the other kind, a reserved state kind and a trailing byte are refused as well.
        Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), progress);
        Kit.AssertViolation(d => StateCodec.DecodePlayerProgressBlob(d), entity);
        var reserved = (byte[])entity.Clone();
        reserved[^2] = 4;
        Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), reserved);
        Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), [.. entity, (byte)0]);
    }
}
