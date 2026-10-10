using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Time.Testing;

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

    [Fact]
    public void IsDrained_AfterDisconnectWithQueuedReliable_FalseUntilDelivered()
    {
        var time = new FakeTimeProvider();
        var network = new InMemoryNetwork(time, new LinkConditions(TimeSpan.FromMilliseconds(100), TimeSpan.Zero, 0, 0, 1));
        using var server = network.CreateTransport("host", TransportOptions.Default);
        using var client = network.CreateTransport("guest", TransportOptions.Default);
        server.Listen(new InMemoryEndpoint("host"));
        var serverEvents = new RecordingEvents();
        var clientEvents = new RecordingEvents();
        client.Connect(new InMemoryEndpoint("host"));
        for (var i = 0; i < 10 && clientEvents.Connected.Count == 0; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            server.Poll(serverEvents);
            client.Poll(clientEvents);
        }

        Assert.True(server.IsDrained);
        server.Send(serverEvents.Connected[0], TransportChannel.ReliableOrdered, [1, 2, 3]);
        server.Disconnect(serverEvents.Connected[0]);

        Assert.False(server.IsDrained);
        time.Advance(TimeSpan.FromMilliseconds(50));
        client.Poll(clientEvents);
        Assert.False(server.IsDrained);

        time.Advance(TimeSpan.FromMilliseconds(100));
        client.Poll(clientEvents);
        Assert.True(server.IsDrained);
        Assert.Equal([1, 2, 3], Assert.Single(clientEvents.Received).Payload);
        Assert.Equal(TransportDisconnectReason.Remote, Assert.Single(clientEvents.Disconnected).Reason);
    }
}
