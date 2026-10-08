using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests.Transport;

public sealed class InMemoryTransportTests : TransportContractTests<InMemoryWorld>
{
    [Fact]
    public void GetRemoteEndpoint_FakeSteamId_ReturnsSteamEndpoint()
    {
        var world = (InMemoryWorld)World;
        var server = world.CreateServer();
        var client = world.CreateClient(fakeSteamId: 76561198000000001UL);
        var clientConn = client.Transport.Connect(world.ServerEndpoint);
        Assert.True(world.PumpUntil(() => server.Events.Connected.Count == 1, TimeSpan.FromSeconds(2)));

        var seenByServer = server.Transport.GetRemoteEndpoint(server.Events.Connected[0]);
        var seenByClient = client.Transport.GetRemoteEndpoint(clientConn);

        Assert.Equal(new SteamEndpoint(76561198000000001UL), seenByServer);
        Assert.Equal(new InMemoryEndpoint("server"), seenByClient);
    }
}
