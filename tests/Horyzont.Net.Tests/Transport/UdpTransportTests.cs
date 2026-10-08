using Horyzont.Net.Transport;
using Horyzont.Net.Transport.Udp;

namespace Horyzont.Net.Tests.Transport;

public sealed class UdpTransportTests : TransportContractTests<UdpWorld>
{
    [Fact]
    public void Disconnect_WhileConnectedEventPending_NeverRaisesConnectedAfterLocal()
    {
        var server = World.CreateServer();
        var client = World.CreateClient();
        var conn = client.Transport.Connect(World.ServerEndpoint);

        // Only the server polls, so the handshake completes on the library thread while the client has not seen it yet.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 500)
        {
            server.Transport.Poll(server.Events);
            Thread.Sleep(5);
        }

        client.Transport.Disconnect(conn);
        World.Pump(TimeSpan.FromMilliseconds(300));

        Assert.Empty(client.Events.Connected);
        Assert.Equal([(conn, TransportDisconnectReason.Local)], client.Events.Disconnected);
    }

    [Fact]
    public void MaxUnreliablePayload_Default_IsAtLeast1150()
    {
        using var transport = new UdpTransport(World.Options);

        Assert.True(transport.MaxUnreliablePayload >= 1150);
    }
}
