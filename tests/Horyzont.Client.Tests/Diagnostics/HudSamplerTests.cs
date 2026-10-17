using Horyzont.Client.Diagnostics;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Diagnostics;

public sealed class HudSamplerTests
{
    [Fact]
    public void Sample_KnownCountersAndSessionReset_ComputesActualBandwidth()
    {
        var time = new FakeTimeProvider();
        var source = new Metrics();
        var sampler = new HudSampler(source, time);
        sampler.NoteRenderDelta(1.0 / 60);
        Assert.False(sampler.Sample().Present.HasFlag(HudFields.Bandwidth));
        time.Advance(TimeSpan.FromSeconds(2));
        source.Sent = 4096;
        source.Received = 8192;
        var sample = sampler.Sample();
        Assert.Equal(2048, sample.SentBytesPerSecond);
        Assert.Equal(4096, sample.ReceivedBytesPerSecond);
        Assert.Equal(60, sample.Fps, 6);
        Assert.Equal(3, sample.ServerTickMilliseconds);
        Assert.Equal(40, sample.RttMilliseconds);
        source.SessionGeneration++;
        Assert.False(sampler.Sample().Present.HasFlag(HudFields.Bandwidth));
        time.Advance(TimeSpan.FromSeconds(1));
        source.Sent = 0;
        Assert.False(sampler.Sample().Present.HasFlag(HudFields.Bandwidth));
    }

    [Fact]
    public void Sample_WarmedUp10000Calls_AllocatesZeroBytes()
    {
        var sampler = new HudSampler(new Metrics(), new FakeTimeProvider());
        for (int i = 0; i < 1000; i++)
        {
            sampler.NoteRenderDelta(0.016);
            sampler.Sample();
        }
        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                sampler.NoteRenderDelta(0.016);
                sampler.Sample();
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
    }

    private sealed class Metrics : IHudMetricsSource
    {
        public long Sent { get; set; }
        public long Received { get; set; }
        public int SessionGeneration { get; set; }
        public bool IsReadOnly => false;
        public bool TryGetServerTick(out ulong tick) { tick = 42; return true; }
        public bool TryGetServerTickMilliseconds(out double milliseconds) { milliseconds = 3; return true; }
        public bool TryGetRtt(out double milliseconds) { milliseconds = 40; return true; }
        public bool TryGetTransportBytes(out long sent, out long received) { sent = Sent; received = Received; return true; }
        public bool TryGetTools(out int pending, out int rejections) { pending = 1; rejections = 2; return true; }
    }
}
