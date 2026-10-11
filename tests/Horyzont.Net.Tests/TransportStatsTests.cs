using Horyzont.Net.Session;
using Horyzont.Net.Tests.Session;

namespace Horyzont.Net.Tests;

public sealed class TransportStatsTests
{
    [Fact]
    public void GetTransportStats_ConnectedPeers_ReportCumulativeBytes()
    {
        using var h = new SessionHarness();
        var client = h.AddClient();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));
        var player = client.Player(h);

        var clientStats = client.Session.GetTransportStats();
        var serverStats = h.Server.GetTransportStats(player);
        Assert.True(clientStats.BytesSent > 0);
        Assert.True(clientStats.BytesReceived > 0);
        Assert.True(serverStats.BytesSent > 0);
        Assert.True(serverStats.BytesReceived > 0);
        Assert.Equal(default, h.Server.GetTransportStats(new Core.Ids.PlayerId(999)));
    }

    [Fact]
    public void GetTransportStats_BeforeConnect_IsZero()
    {
        using var h = new SessionHarness();
        var client = h.AddClient();
        client.Session.Disconnect();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));
        Assert.Equal(default, client.Session.GetTransportStats());
    }
}
