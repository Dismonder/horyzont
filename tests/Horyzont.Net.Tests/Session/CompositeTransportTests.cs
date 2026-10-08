using System.Diagnostics;

using Horyzont.Core.Ids;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Net.Transport.Udp;

namespace Horyzont.Net.Tests.Session;

public sealed class CompositeTransportTests
{
    [Fact(Timeout = 30_000)]
    public void CompositeTransport_MemoryAndUdp_BothReachInGame()
    {
        var options = new TransportOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250));
        var network = new InMemoryNetwork(TimeProvider.System);
        using var memoryServer = network.CreateTransport("host", options);
        using var udpServer = new UdpTransport(options);
        using var composite = new CompositeTransport([memoryServer, udpServer]);
        composite.Listen(new InMemoryEndpoint("host"));
        composite.Listen(new UdpEndpoint("127.0.0.1", 0));
        var serverHooks = new RecordingServerHooks();
        var server = new ServerSession(composite, new ServerOptions { BuildHash = SessionHarness.Build }, serverHooks, TimeProvider.System);

        using var memoryClientTransport = network.CreateTransport("local", options);
        using var udpClientTransport = new UdpTransport(options);
        var memoryHooks = new RecordingClientHooks();
        var udpHooks = new RecordingClientHooks();
        var clientOptions = new ClientOptions { BuildHash = SessionHarness.Build };
        var memoryClient = new ClientSession(memoryClientTransport, clientOptions, memoryHooks, TimeProvider.System);
        var udpClient = new ClientSession(udpClientTransport, clientOptions, udpHooks, TimeProvider.System);
        memoryClient.Connect(new InMemoryEndpoint("host"), new PlatformIdentity(IdentityPlatform.Dev, 1), "local");
        udpClient.Connect(new UdpEndpoint("127.0.0.1", udpServer.LocalPort), new PlatformIdentity(IdentityPlatform.Dev, 2), "remote");

        ulong tick = 0;
        bool Pump(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                if (watch.Elapsed > TimeSpan.FromSeconds(10))
                {
                    return false;
                }

                memoryClient.Poll();
                udpClient.Poll();
                server.Poll(new Tick(++tick));
                Thread.Sleep(1);
            }

            return true;
        }

        Assert.True(Pump(() => memoryClient.State == ClientState.Loading && udpClient.State == ClientState.Loading));
        memoryClient.SendReady();
        udpClient.SendReady();
        Assert.True(Pump(() => serverHooks.Ready.Count == 2));

        Assert.Equal(ClientState.InGame, memoryClient.State);
        Assert.Equal(ClientState.InGame, udpClient.State);
        Assert.Equal(2, server.PlayersInGame.Count);
        Assert.NotEqual(memoryHooks.Hello!.YourPlayer, udpHooks.Hello!.YourPlayer);

        server.Shutdown(Core.Protocol.DisconnectCode.HostLeft);
        Assert.True(Pump(() => server.IsShutdownComplete && memoryClient.State == ClientState.Disconnected && udpClient.State == ClientState.Disconnected));
    }

    [Fact]
    public void CompositeTransport_SameInnerIds_MapsToUniqueConnections()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var network = new InMemoryNetwork(time);
        var options = TransportOptions.Default;
        using var a = network.CreateTransport("a", options);
        using var b = network.CreateTransport("b", options);
        using var composite = new CompositeTransport([a, b]);
        composite.Listen(new InMemoryEndpoint("a"));
        Assert.Throws<ArgumentException>(() => composite.Listen(new UdpEndpoint("127.0.0.1", 0)));
        b.Listen(new InMemoryEndpoint("b"));
        using var c1 = network.CreateTransport("c1", options);
        using var c2 = network.CreateTransport("c2", options);
        c1.Connect(new InMemoryEndpoint("a"));
        c2.Connect(new InMemoryEndpoint("b"));
        var events = new Transport.RecordingEvents();

        composite.Poll(events);

        Assert.Equal(2, events.Connected.Count);
        Assert.NotEqual(events.Connected[0], events.Connected[1]);
        Assert.Equal(new InMemoryEndpoint("c1"), composite.GetRemoteEndpoint(events.Connected[0]));
        Assert.Equal(new InMemoryEndpoint("c2"), composite.GetRemoteEndpoint(events.Connected[1]));

        composite.Send(events.Connected[1], TransportChannel.ReliableOrdered, [1, 2, 3]);
        var c2Events = new Transport.RecordingEvents();
        c2.Poll(c2Events);
        Assert.Equal([1, 2, 3], Assert.Single(c2Events.Received).Payload);

        composite.Disconnect(events.Connected[0]);
        composite.Poll(events);
        Assert.Equal((events.Connected[0], TransportDisconnectReason.Local), Assert.Single(events.Disconnected));
        Assert.Throws<ArgumentException>(() => composite.GetRemoteEndpoint(events.Connected[0]));
    }

    [Fact]
    public void Send_KnownConnectionAboveCompositeLimit_Throws()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var network = new InMemoryNetwork(time);
        var options = TransportOptions.Default;
        using var memory = network.CreateTransport("host", options);
        using var udp = new UdpTransport(options);
        using var composite = new CompositeTransport([memory, udp]);
        composite.Listen(new InMemoryEndpoint("host"));
        using var client = network.CreateTransport("local", options);
        client.Connect(new InMemoryEndpoint("host"));
        var events = new Transport.RecordingEvents();
        composite.Poll(events);
        var connection = Assert.Single(events.Connected);
        Assert.True(memory.MaxUnreliablePayload > composite.MaxUnreliablePayload, "the test needs inner transports with different limits");

        Assert.Throws<ArgumentException>(() => composite.Send(connection, TransportChannel.Unreliable, new byte[composite.MaxUnreliablePayload + 1]));
        if (memory.MaxReliablePayload > composite.MaxReliablePayload)
        {
            Assert.Throws<ArgumentException>(() => composite.Send(connection, TransportChannel.ReliableOrdered, new byte[composite.MaxReliablePayload + 1]));
        }

        composite.Send(connection, TransportChannel.Unreliable, new byte[composite.MaxUnreliablePayload]);
        var clientEvents = new Transport.RecordingEvents();
        client.Poll(clientEvents);
        Assert.Equal(composite.MaxUnreliablePayload, Assert.Single(clientEvents.Received).Payload.Length);
    }
}
