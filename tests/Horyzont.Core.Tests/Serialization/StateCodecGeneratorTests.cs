using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// K6-34, D12: the state of an oxygen generator (kind 5) in blob 21 and in operation 40, without a new version of either: <c>05 varuint input, flag (0 idle, 1 running), [varuint start]</c>.
/// Kinds 4, 6 and 7 stay reserved.
/// </summary>
public sealed class StateCodecGeneratorTests
{
    private static readonly EntityId _construct = new(5);
    private static readonly CellPos _cell = new(1, -1, 0);

    private static BlockEntityRecord Record(OxygenGeneratorBlockState state) => new(_construct, _cell, state);

    private static BlockEntitySetOp SetOp(OxygenGeneratorBlockState? state) => new(_construct, _cell, 3, state);

    [Fact]
    public void GeneratorState_GoldenBytes_IdleAndRunning()
    {
        // Blob 21: kind byte 15, version 01, the construct, the cell (05 02 01 00), then the state.
        Assert.Equal("15 01 05 02 01 00 05 09 00", Kit.ToHex(StateCodec.EncodeBlockEntityBlob(Record(new OxygenGeneratorBlockState(new ContainerId(9), null)))));
        Assert.Equal("15 01 05 02 01 00 05 09 01 64", Kit.ToHex(StateCodec.EncodeBlockEntityBlob(Record(new OxygenGeneratorBlockState(new ContainerId(9), new Tick(100))))));
        Assert.Equal("15 01 05 02 01 00 05 09 01 00", Kit.ToHex(StateCodec.EncodeBlockEntityBlob(Record(new OxygenGeneratorBlockState(new ContainerId(9), new Tick(0))))));
        Assert.Equal(
            "15 01 05 02 01 00 05 FF FF FF FF FF FF FF FF FF 01 01 FF FF FF FF FF FF FF FF FF 01",
            Kit.ToHex(StateCodec.EncodeBlockEntityBlob(Record(new OxygenGeneratorBlockState(new ContainerId(ulong.MaxValue), new Tick(ulong.MaxValue))))));

        // Operation 40: 0x28, version 01, the construct, the cell, the block type (3 here), then the same state bytes.
        Assert.Equal("28 01 05 02 01 00 03 05 09 01 64", Kit.ToHex(Kit.Encode(SetOp(new OxygenGeneratorBlockState(new ContainerId(9), new Tick(100))))));
        Assert.Equal("28 01 05 02 01 00 03 05 09 00", Kit.ToHex(Kit.Encode(SetOp(new OxygenGeneratorBlockState(new ContainerId(9), null)))));
    }

    [Fact]
    public void GeneratorState_RoundTrip()
    {
        foreach (var state in new[]
        {
            new OxygenGeneratorBlockState(new ContainerId(9), null),
            new OxygenGeneratorBlockState(new ContainerId(9), new Tick(100)),
            new OxygenGeneratorBlockState(new ContainerId(9), new Tick(0)),
            new OxygenGeneratorBlockState(new ContainerId(ulong.MaxValue), new Tick(ulong.MaxValue)),
        })
        {
            var blob = StateCodec.EncodeBlockEntityBlob(Record(state));
            DeepAssert.Equal(Record(state), StateCodec.DecodeBlockEntityBlob(blob));
            Assert.Equal(blob, StateCodec.EncodeBlockEntityBlob(StateCodec.DecodeBlockEntityBlob(blob)));

            var frame = Kit.Encode(SetOp(state));
            DeepAssert.Equal(SetOp(state), Kit.DecodeOperation(frame));
            Assert.Equal(frame, Kit.Encode(Kit.DecodeOperation(frame)));
        }

        // Clearing a generator state is the usual operation 40 without a state.
        var clear = Kit.Encode(SetOp(null));
        DeepAssert.Equal(SetOp(null), Kit.DecodeOperation(clear));
    }

    [Fact]
    public void GeneratorState_UnknownFlag_Throws()
    {
        // The accepted counterparts: idle, and running with a start.
        Assert.Equal(new Tick(100), Assert.IsType<OxygenGeneratorBlockState>(StateCodec.DecodeBlockEntityBlob(Kit.Hex("15 01 05 02 01 00 05 09 01 64")).State).Started);
        Assert.Null(Assert.IsType<OxygenGeneratorBlockState>(StateCodec.DecodeBlockEntityBlob(Kit.Hex("15 01 05 02 01 00 05 09 00")).State).Started);
        Assert.IsType<BlockEntitySetOp>(Kit.DecodeOperation(Kit.Hex("28 01 05 02 01 00 03 05 09 01 64")));

        foreach (var flag in new[] { "02", "03", "80", "FF" })
        {
            Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), Kit.Hex("15 01 05 02 01 00 05 09 " + flag + " 64"));
            Kit.AssertOperationViolation("28 01 05 02 01 00 03 05 09 " + flag + " 64");
        }

        // A running generator without a start, an idle one with a start, and a frame that stops after the input.
        Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), Kit.Hex("15 01 05 02 01 00 05 09 01"));
        Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), Kit.Hex("15 01 05 02 01 00 05 09 00 64"));
        Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), Kit.Hex("15 01 05 02 01 00 05 09"));
        Kit.AssertOperationViolation("28 01 05 02 01 00 03 05 09 01");
        Kit.AssertOperationViolation("28 01 05 02 01 00 03 05 09 00 64");
    }

    [Fact]
    public void Kind6_StillReserved_Throws()
    {
        // Kind 5 reads; 4 (teleport), 6 (planter) and 7 (cargo hold) belong to later packages and are still refused with the same bytes after the kind.
        Assert.IsType<BlockEntitySetOp>(Kit.DecodeOperation(Kit.Hex("28 01 05 02 01 00 03 05 09 00")));

        foreach (var kind in new[] { "04", "06", "07", "08" })
        {
            Kit.AssertViolation(d => StateCodec.DecodeBlockEntityBlob(d), Kit.Hex("15 01 05 02 01 00 " + kind + " 09 00"));
            Kit.AssertOperationViolation("28 01 05 02 01 00 03 " + kind + " 09 00");
        }
    }
}
