using System.Diagnostics;
using System.Net;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Net.Session;
using Horyzont.Net.Tests.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.Udp;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Net.Tests.Transport;

/// <summary>E6 (drain) and E7 (closing deadline and limit) over real loopback sockets; every test has a time limit.</summary>
public sealed class UdpClosingTests
{
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(10);

    [Fact]
    public void IsDrained_AfterDisconnectWithReliableData_FalseUntilDelivered()
    {
        using var rig = new Rig(new TransportOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100)));
        var client = rig.AddClient();
        Assert.True(rig.PumpUntil(() => rig.ServerEvents.Connected.Count == 1 && client.Events.Connected.Count == 1));
        var connection = rig.ServerEvents.Connected[0];
        Assert.True(rig.Server.IsDrained);

        var payload = new byte[200_000];
        rig.Server.Send(connection, TransportChannel.ReliableOrdered, payload);
        rig.Server.Disconnect(connection);

        Assert.False(rig.Server.IsDrained);
        Assert.True(rig.PumpUntil(() => rig.Server.IsDrained));
        Assert.True(rig.PumpUntil(() => client.Events.Disconnected.Count == 1));
        Assert.Equal(payload.Length, Assert.Single(client.Events.Received).Payload.Length);
    }

    [Fact]
    public void Closing_PeerNeverAcks_ForcedAfterCloseTimeout()
    {
        var time = new FakeTimeProvider();
        var options = new TransportOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)) { CloseTimeout = TimeSpan.FromSeconds(5) };
        using var rig = new Rig(options, time);
        var client = rig.AddClient();
        Assert.True(rig.PumpUntil(() => rig.ServerEvents.Connected.Count == 1 && client.Events.Connected.Count == 1));
        var connection = rig.ServerEvents.Connected[0];

        rig.Relay.Partitioned = true;
        rig.Server.Send(connection, TransportChannel.ReliableOrdered, [1, 2, 3]);
        rig.Server.Disconnect(connection);
        rig.Server.Poll(rig.ServerEvents);
        Assert.Equal(1, rig.Server.ClosingCount);
        Assert.False(rig.Server.IsDrained);

        time.Advance(TimeSpan.FromSeconds(4));
        rig.Server.Poll(rig.ServerEvents);
        Assert.Equal(1, rig.Server.ClosingCount);

        time.Advance(TimeSpan.FromSeconds(2));
        rig.Server.Poll(rig.ServerEvents);
        Assert.Equal(0, rig.Server.ClosingCount);
        Assert.True(rig.Server.IsDrained);
    }

    [Fact]
    public void Closing_OverLimit_OldestForced()
    {
        var options = new TransportOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1))
        {
            CloseTimeout = TimeSpan.FromMinutes(5),
            MaxClosingConnections = 2,
        };
        using var rig = new Rig(options);
        var clients = Enumerable.Range(0, 3).Select(_ => rig.AddClient()).ToList();
        Assert.True(rig.PumpUntil(() => rig.ServerEvents.Connected.Count == 3 && clients.All(c => c.Events.Connected.Count == 1)));

        rig.Relay.Partitioned = true;
        foreach (var connection in rig.ServerEvents.Connected)
        {
            rig.Server.Send(connection, TransportChannel.ReliableOrdered, [9]);
            rig.Server.Disconnect(connection);
        }

        Assert.Equal(2, rig.Server.ClosingCount);
        Assert.False(rig.Server.IsDrained);
    }

    [Fact]
    public void HostShutdown_ClientSeesHostLeft()
    {
        using var rig = new Rig(new TransportOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100)));
        var serverHooks = new RecordingServerHooks();
        var server = new ServerSession(rig.Server, new ServerOptions { BuildHash = SessionHarness.Build }, serverHooks, TimeProvider.System);
        using var clientTransport = new UdpTransport(rig.Options);
        var clientHooks = new RecordingClientHooks();
        var client = new ClientSession(clientTransport, new ClientOptions { BuildHash = SessionHarness.Build }, clientHooks, TimeProvider.System);
        clientHooks.Session = client;
        client.Connect(new UdpEndpoint("127.0.0.1", rig.Relay.Port), new PlatformIdentity(IdentityPlatform.Dev, 5), "remote");
        ulong tick = 0;
        var watch = Stopwatch.StartNew();
        while (client.State != ClientState.Loading && watch.Elapsed < _limit)
        {
            client.Poll();
            server.Poll(new Tick(++tick));
            Thread.Sleep(1);
        }

        Assert.Equal(ClientState.Loading, client.State);

        // The client is not polled while the host drains and disposes its transport: it must still learn HostLeft.
        server.Shutdown(DisconnectCode.HostLeft);
        watch.Restart();
        while (!(server.IsShutdownComplete && rig.Server.IsDrained) && watch.Elapsed < _limit)
        {
            server.Poll(new Tick(++tick));
            Thread.Sleep(1);
        }

        Assert.True(server.IsShutdownComplete && rig.Server.IsDrained);
        rig.Server.Dispose();
        watch.Restart();
        while (client.State != ClientState.Disconnected && watch.Elapsed < _limit)
        {
            client.Poll();
            Thread.Sleep(1);
        }

        Assert.Equal(DisconnectCode.HostLeft, Assert.Single(clientHooks.Disconnections).Code);
    }

    private sealed class Rig : IDisposable
    {
        private readonly List<TransportNode> _clients = [];

        public Rig(TransportOptions options, TimeProvider? serverTime = null)
        {
            Options = options;
            Server = new UdpTransport(options, null, serverTime);
            Server.Listen(new UdpEndpoint("127.0.0.1", 0));
            Relay = new UdpLossyRelay(new IPEndPoint(IPAddress.Loopback, Server.LocalPort), seed: 7);
        }

        public TransportOptions Options { get; }

        public UdpTransport Server { get; }

        public UdpLossyRelay Relay { get; }

        public RecordingEvents ServerEvents { get; } = new();

        public TransportNode AddClient()
        {
            var transport = new UdpTransport(Options);
            transport.Connect(new UdpEndpoint("127.0.0.1", Relay.Port));
            var node = new TransportNode(transport, new RecordingEvents());
            _clients.Add(node);
            return node;
        }

        public bool PumpUntil(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed >= _limit)
                {
                    return false;
                }

                Server.Poll(ServerEvents);
                foreach (var client in _clients)
                {
                    client.Transport.Poll(client.Events);
                }

                Thread.Sleep(1);
            }

            return true;
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client.Transport.Dispose();
            }

            Server.Dispose();
            Relay.Dispose();
        }
    }
}
