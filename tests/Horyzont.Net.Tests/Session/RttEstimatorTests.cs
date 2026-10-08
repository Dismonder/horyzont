using Horyzont.Net.Session;

namespace Horyzont.Net.Tests.Session;

public sealed class RttEstimatorTests
{
    [Fact]
    public void AddSample_First_SetsSmoothedAndHalfDeviation()
    {
        var rtt = new RttEstimator();

        rtt.AddSample(TimeSpan.FromMilliseconds(100));

        Assert.True(rtt.HasSamples);
        Assert.Equal(TimeSpan.FromMilliseconds(100), rtt.Smoothed);
        Assert.Equal(TimeSpan.FromMilliseconds(50), rtt.Deviation);
    }

    [Fact]
    public void AddSample_Next_UsesAlphaOneEighth()
    {
        var rtt = new RttEstimator();
        rtt.AddSample(TimeSpan.FromMilliseconds(100));

        rtt.AddSample(TimeSpan.FromMilliseconds(180));

        Assert.Equal(110, rtt.Smoothed.TotalMilliseconds, 3);
        Assert.Equal(57.5, rtt.Deviation.TotalMilliseconds, 3); // 0.75 * 50 + 0.25 * |100 - 180|
    }

    [Fact]
    public void AddSample_Negative_IsIgnored()
    {
        var rtt = new RttEstimator();

        rtt.AddSample(TimeSpan.FromMilliseconds(-5));

        Assert.False(rtt.HasSamples);
        Assert.Equal(TimeSpan.Zero, rtt.Smoothed);
    }
}
