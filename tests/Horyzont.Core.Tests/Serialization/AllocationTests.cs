using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Allocation budgets for the per-tick snapshot path (docs/ZASADY.md point 6).</summary>
public sealed class AllocationTests
{
    [Fact]
    public void Write_SnapshotWithEightFramesAnd22Entities_IntoAnExistingWriter_DoesNotAllocate()
    {
        var snapshot = MaxMessages.Snapshot();
        var writer = new ByteWriter(2048);
        for (var i = 0; i < 20; i++)
        {
            writer.Reset();
            MessageCodec.Write(writer, snapshot);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            writer.Reset();
            MessageCodec.Write(writer, snapshot);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(1037, writer.Length);
    }

    [Fact]
    public void Write_GoldenSnapshot_IntoAnExistingWriter_DoesNotAllocate()
    {
        var snapshot = GoldenBytesTests.GoldenSnapshot();
        var writer = new ByteWriter(256);
        for (var i = 0; i < 20; i++)
        {
            writer.Reset();
            MessageCodec.Write(writer, snapshot);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            writer.Reset();
            MessageCodec.Write(writer, snapshot);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Read_SnapshotWithEightFramesAnd22Entities_AllocatesAtMostEightKiB()
    {
        var frame = Kit.Encode(MaxMessages.Snapshot());
        for (var i = 0; i < 20; i++)
        {
            _ = MessageCodec.Read(frame);
        }

        var worst = 0L;
        for (var i = 0; i < 50; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var decoded = MessageCodec.Read(frame);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            worst = Math.Max(worst, allocated);
            Assert.IsType<Snapshot>(decoded);
        }

        Assert.True(worst <= 8 * 1024, $"decoding allocated {worst} bytes");
    }
}
