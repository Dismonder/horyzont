using Horyzont.Client.Platform;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Platform;

public sealed class CompositionPlatformTests
{
    [Fact]
    public void NoCompositionPlatform_DefaultPaths_PreservesTransportsAndDefersToUdp()
    {
        var time = new FakeTimeProvider();
        var network = new InMemoryNetwork(time);
        using var transport = network.CreateTransport("test", TransportOptions.Default);
        var platform = NoCompositionPlatform.Instance;
        var transports = new List<ITransport> { transport };
        var listen = new List<TransportEndpoint> { new InMemoryEndpoint("test") };
        platform.ConfigureServerTransports(transports, listen);
        Assert.Same(transport, Assert.Single(transports));
        Assert.Equal(new InMemoryEndpoint("test"), Assert.Single(listen));
        var calls = 0;
        Assert.Same(transport, platform.CreateClientTransport(() => { calls++; return transport; }));
        Assert.Equal(1, calls);
        var client = new ClientRuntime(transport, new ClientOptions(), false, time, NullLoggerFactory.Instance);
        Assert.False(platform.TryConnectClient(client));
        Assert.Equal(ClientState.Disconnected, client.Session.State);
    }

    [Fact]
    public void PlatformSession_ReleaseAndProcessCleanup_NotifiesOnceAndRetriesCleanup()
    {
        var platform = new Platform();
        var session = new PlatformSession(platform);
        var calls = 0;
        bool Cleanup() => ++calls == 2;
        Assert.False(session.TryProcessCleanup(Cleanup));
        Assert.Equal(0, calls);
        session.OnWorldStarted(new WorldId(Guid.NewGuid()), 8, true);
        session.OnShutdownRequested();
        session.OnShutdownRequested();
        Assert.False(session.TryProcessCleanup(Cleanup));
        session.OnTransportsReleased();
        session.OnTransportsReleased();
        Assert.False(session.TryProcessCleanup(Cleanup));
        Assert.True(session.TryProcessCleanup(Cleanup));
        Assert.True(session.TryProcessCleanup(Cleanup));
        Assert.Equal(2, calls);
        Assert.Equal(["world:8:True", "shutdown", "released"], platform.Calls);
    }

    [Fact]
    public void ReleaseTrackedTransport_DisposeFailure_DoesNotReportRelease()
    {
        var inner = new Transport { Fail = true };
        var tracked = new ReleaseTrackedTransport(inner);
        Assert.Throws<InvalidOperationException>(tracked.Dispose);
        Assert.False(tracked.IsReleased);
        inner.Fail = false;
        tracked.Dispose();
        Assert.True(tracked.IsReleased);
        tracked.Dispose();
        Assert.Equal(2, inner.Disposals);
    }

    private sealed class Platform : ICompositionPlatform
    {
        public List<string> Calls { get; } = [];
        public void ConfigureServerTransports(IList<ITransport> transports, IList<TransportEndpoint> listen) { }
        public ITransport CreateClientTransport(Func<ITransport> fallback) => fallback();
        public bool TryConnectClient(ClientRuntime client) => true;
        public void OnWorldStarted(WorldId world, int capacity, bool passwordRequired) => Calls.Add($"world:{capacity}:{passwordRequired}");
        public void OnShutdownRequested() => Calls.Add("shutdown");
        public void OnTransportsReleased() => Calls.Add("released");
    }

    private sealed class Transport : ITransport
    {
        public bool Fail { get; set; }
        public int Disposals { get; private set; }
        public int MaxReliablePayload => 262144;
        public int MaxUnreliablePayload => 1150;
        public bool IsDrained => true;
        public void Dispose()
        {
            Disposals++;
            if (Fail) { throw new InvalidOperationException("dispose failed"); }
        }
        public void Listen(TransportEndpoint endpoint) { }
        public ConnectionId Connect(TransportEndpoint endpoint) => default;
        public void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) { }
        public void Disconnect(ConnectionId connection) { }
        public void Poll(ITransportEvents events) { }
        public TransportStats GetStats(ConnectionId connection) => default;
        public TransportEndpoint GetRemoteEndpoint(ConnectionId connection) => new InMemoryEndpoint("test");
    }
}
