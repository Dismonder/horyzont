using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests.Transport;

public sealed class TransportOptionsTests
{
    [Fact]
    public void TransportOptions_KeepAliveAboveQuarterTimeout_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TransportOptions(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(1001)));
    }

    [Fact]
    public void TransportOptions_KeepAliveExactlyQuarterTimeout_IsAccepted()
    {
        var options = new TransportOptions(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(1), options.KeepAlive);
    }

    [Fact]
    public void Default_IsValid_TenSecondsAndOneSecond()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), TransportOptions.Default.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(1), TransportOptions.Default.KeepAlive);
    }
}
