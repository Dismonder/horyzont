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

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++)
            {
                writer.Reset();
                MessageCodec.Write(writer, snapshot);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        var allocated = smallest;
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

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++)
            {
                writer.Reset();
                MessageCodec.Write(writer, snapshot);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
    }

    [Fact]
    public void Read_SnapshotWithEightFramesAnd22Entities_AllocatesAtMostEightKiB()
    {
        var frame = Kit.Encode(MaxMessages.Snapshot());
        for (var i = 0; i < 20; i++)
        {
            _ = MessageCodec.Read(frame);
        }

        // Keep the worst of 50 reads per window, taking the smallest window to exclude isolated tiered JIT allocations.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5; window++)
        {
            var worst = 0L;
            for (var i = 0; i < 50; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var decoded = MessageCodec.Read(frame);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                worst = Math.Max(worst, allocated);
                Assert.IsType<Snapshot>(decoded);
            }

            smallest = Math.Min(smallest, worst);
        }

        Assert.True(smallest <= 8 * 1024, $"decoding allocated {smallest} bytes");
    }
}
