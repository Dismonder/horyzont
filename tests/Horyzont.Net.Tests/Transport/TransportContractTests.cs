// The pump loops carry their own deadline, so the test timeout cannot leave a runaway loop behind.
#pragma warning disable xUnit1069
using System.Buffers.Binary;

using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests.Transport;

public abstract class TransportContractTests<TFactory> : IDisposable
    where TFactory : ITransportWorldFactory
{
    private static readonly TimeSpan _longWait = TimeSpan.FromSeconds(8);

    protected TransportContractTests()
    {
        World = TFactory.Create();
    }

    private TransportNode? _server;

    protected ITransportWorld World { get; }

    public void Dispose()
    {
        World.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact(Timeout = 10_000)]
    public void Connect_ServerListening_BothSidesReportConnected()
    {
        var (server, client, _, _) = ConnectPair();

        Assert.Single(server.Events.Connected);
        Assert.Single(client.Events.Connected);
    }

    [Fact(Timeout = 10_000)]
    public void SendReliable_1000Messages_ArriveInOrderOnce()
    {
        Server();
        World.SetLoss(0.10, 0.05);
        var (server, client, _, clientConn) = ConnectPair();

        for (var i = 0; i < 1000; i++)
        {
            client.Transport.Send(clientConn, TransportChannel.ReliableOrdered, Indexed(i, 64));
        }

        Assert.True(World.PumpUntil(() => server.Events.ReceivedOn(TransportChannel.ReliableOrdered).Count() >= 1000, _longWait));
        World.Pump(TimeSpan.FromMilliseconds(200));

        var received = server.Events.ReceivedOn(TransportChannel.ReliableOrdered).ToList();
        Assert.Equal(1000, received.Count);
        for (var i = 0; i < received.Count; i++)
        {
            Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(received[i].Payload));
            Assert.Equal(64, received[i].Payload.Length);
        }
    }

    [Fact(Timeout = 10_000)]
    public void SendReliable_256KiB_ArrivesIntact()
    {
        var (server, client, _, clientConn) = ConnectPair();
        var payload = new byte[256 * 1024];
        new Random(5).NextBytes(payload);

        client.Transport.Send(clientConn, TransportChannel.ReliableOrdered, payload);

        Assert.True(World.PumpUntil(() => server.Events.Received.Count >= 1, _longWait));
        Assert.Equal(payload, server.Events.Received[0].Payload);
    }

    [Fact(Timeout = 10_000)]
    public void SendUnreliable_OverLimit_Throws()
    {
        var (server, client, _, clientConn) = ConnectPair();
        var tooBig = new byte[client.Transport.MaxUnreliablePayload + 1];

        Assert.Throws<ArgumentException>(() => client.Transport.Send(clientConn, TransportChannel.Unreliable, tooBig));
        World.Pump(TimeSpan.FromMilliseconds(300));

        Assert.Empty(server.Events.Received);
    }

    [Fact(Timeout = 10_000)]
    public void SendUnreliable_1150Bytes_Arrives()
    {
        var (server, client, _, clientConn) = ConnectPair();
        var payload = new byte[1150];
        new Random(6).NextBytes(payload);

        client.Transport.Send(clientConn, TransportChannel.Unreliable, payload);

        Assert.True(World.PumpUntil(() => server.Events.Received.Count >= 1, _longWait));
        Assert.Equal(payload, server.Events.Received[0].Payload);
        Assert.Equal(TransportChannel.Unreliable, server.Events.Received[0].Channel);
    }

    [Fact(Timeout = 10_000)]
    public void SendUnreliable_UnderLoss_DeliversIntactSubset()
    {
        var (server, client, _, clientConn) = ConnectPair();
        World.SetLoss(0.20, 0);

        const int total = 300;
        for (var i = 0; i < total; i++)
        {
            client.Transport.Send(clientConn, TransportChannel.Unreliable, Indexed(i, 200));
        }

        World.Pump(TimeSpan.FromSeconds(1));

        var received = server.Events.ReceivedOn(TransportChannel.Unreliable).ToList();
        Assert.NotEmpty(received);
        Assert.True(received.Count < total, "20% loss should drop at least one message");
        foreach (var message in received)
        {
            Assert.Equal(200, message.Payload.Length);
            var index = BinaryPrimitives.ReadInt32LittleEndian(message.Payload);
            Assert.Equal(Indexed(index, 200), message.Payload);
        }
    }

    [Fact(Timeout = 10_000)]
    public void Disconnect_Local_RemoteNotifiedWithin1s()
    {
        var (server, client, serverConn, clientConn) = ConnectPair();

        client.Transport.Disconnect(clientConn);

        Assert.True(World.PumpUntil(
            () => server.Events.Disconnected.Count == 1 && client.Events.Disconnected.Count == 1,
            TimeSpan.FromSeconds(1)));
        Assert.Equal((serverConn, TransportDisconnectReason.Remote), server.Events.Disconnected[0]);
        Assert.Equal((clientConn, TransportDisconnectReason.Local), client.Events.Disconnected[0]);
    }

    [Fact(Timeout = 10_000)]
    public void RemoteVanishes_NoTraffic_TimesOut()
    {
        var (server, client, _, _) = ConnectPair();

        World.Partition();

        Assert.True(World.PumpUntil(
            () => server.Events.Disconnected.Count == 1 && client.Events.Disconnected.Count == 1,
            World.Options.Timeout + TimeSpan.FromSeconds(1)));
        Assert.Equal(TransportDisconnectReason.Timeout, server.Events.Disconnected[0].Reason);
        Assert.Equal(TransportDisconnectReason.Timeout, client.Events.Disconnected[0].Reason);
    }

    [Fact(Timeout = 10_000)]
    public void SendReliable_ThenDisconnectOverLossyLink_DeliversEverythingBeforeDisconnected()
    {
        Server();
        World.SetLoss(0.30, 0);
        var (server, client, _, clientConn) = ConnectPair();
        const int total = 20;

        for (var i = 0; i < total; i++)
        {
            client.Transport.Send(clientConn, TransportChannel.ReliableOrdered, Indexed(i, i == total - 1 ? 100 * 1024 : 300));
        }

        client.Transport.Disconnect(clientConn);

        Assert.True(World.PumpUntil(() => server.Events.Disconnected.Count == 1, _longWait));
        var received = server.Events.ReceivedOn(TransportChannel.ReliableOrdered).ToList();
        Assert.Equal(total, received.Count);
        for (var i = 0; i < total; i++)
        {
            Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(received[i].Payload));
        }
    }

    [Fact(Timeout = 10_000)]
    public void SendReliable_AcrossShortPartition_ArrivesInOrderAfterHeal()
    {
        var (server, client, _, clientConn) = ConnectPair();
        var half = TimeSpan.FromMilliseconds(100);

        SendRange(client, clientConn, 0, 3);
        World.Pump(half);
        World.Partition();
        SendRange(client, clientConn, 3, 7);
        World.Pump(half);
        World.Heal();
        SendRange(client, clientConn, 7, 10);

        Assert.True(World.PumpUntil(() => server.Events.ReceivedOn(TransportChannel.ReliableOrdered).Count() >= 10, _longWait));
        World.Pump(TimeSpan.FromMilliseconds(200));
        var received = server.Events.ReceivedOn(TransportChannel.ReliableOrdered).ToList();
        Assert.Equal(10, received.Count);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(received[i].Payload));
        }

        Assert.Empty(server.Events.Disconnected);
    }

    [Fact(Timeout = 10_000)]
    public void Connect_ImmediatelyAfterDisconnect_NewConnectionEstablishes()
    {
        var (server, client, _, firstConn) = ConnectPair();

        client.Transport.Disconnect(firstConn);
        var secondConn = client.Transport.Connect(World.ServerEndpoint);

        Assert.True(World.PumpUntil(() => client.Events.Connected.Contains(secondConn) && server.Events.Connected.Count == 2, _longWait));
        World.Pump(TimeSpan.FromMilliseconds(300));
        Assert.NotEqual(firstConn, secondConn);
        Assert.DoesNotContain(client.Events.Disconnected, d => d.Connection == secondConn);

        client.Transport.Send(secondConn, TransportChannel.ReliableOrdered, new byte[] { 7 });
        Assert.True(World.PumpUntil(() => server.Events.Received.Count == 1, _longWait));
    }

    [Fact(Timeout = 10_000)]
    public void Send_AfterDisconnect_IsIgnored()
    {
        var (server, client, serverConn, clientConn) = ConnectPair();
        client.Transport.Disconnect(clientConn);
        Assert.True(World.PumpUntil(() => server.Events.Disconnected.Count == 1, TimeSpan.FromSeconds(2)));

        var exception = Record.Exception(() =>
        {
            client.Transport.Send(clientConn, TransportChannel.ReliableOrdered, new byte[] { 1, 2, 3 });
            server.Transport.Send(serverConn, TransportChannel.Unreliable, new byte[] { 1, 2, 3 });
            client.Transport.Send(new ConnectionId(987654), TransportChannel.ReliableOrdered, new byte[] { 1 });
        });

        Assert.Null(exception);
    }

    [Fact(Timeout = 10_000)]
    public void Connect_EightClients_DistinctConnectionIds()
    {
        var server = Server();
        var clients = Enumerable.Range(0, 8).Select(_ => World.CreateClient()).ToList();
        foreach (var c in clients)
        {
            c.Transport.Connect(World.ServerEndpoint);
        }


        Assert.True(World.PumpUntil(
            () => server.Events.Connected.Count == 8 && clients.All(c => c.Events.Connected.Count == 1),
            _longWait));

        Assert.Equal(8, server.Events.Connected.Distinct().Count());
    }

    [Fact(Timeout = 10_000)]
    public void Connect_AfterDisconnect_ConnectionIdNotReused()
    {
        var (server, client, _, firstClientConn) = ConnectPair();
        client.Transport.Disconnect(firstClientConn);
        Assert.True(World.PumpUntil(() => server.Events.Disconnected.Count == 1, TimeSpan.FromSeconds(2)));

        var secondClientConn = client.Transport.Connect(World.ServerEndpoint);
        Assert.True(World.PumpUntil(() => server.Events.Connected.Count == 2, _longWait));

        Assert.NotEqual(firstClientConn, secondClientConn);
        Assert.NotEqual(server.Events.Connected[0], server.Events.Connected[1]);
    }

    [Fact(Timeout = 10_000)]
    public void Poll_Idle_DoesNotAllocate()
    {
        var (_, client, _, _) = ConnectPair();
        for (var i = 0; i < 200; i++)
        {
            client.Transport.Poll(client.Events);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            client.Transport.Poll(client.Events);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    private static void SendRange(TransportNode node, ConnectionId connection, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            node.Transport.Send(connection, TransportChannel.ReliableOrdered, Indexed(i, 32));
        }
    }

    private static byte[] Indexed(int index, int length)
    {
        var data = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(data, index);
        for (var i = 4; i < length; i++)
        {
            data[i] = (byte)(index + i);
        }

        return data;
    }

    private TransportNode Server() => _server ??= World.CreateServer();

    private (TransportNode Server, TransportNode Client, ConnectionId ServerConn, ConnectionId ClientConn) ConnectPair()
    {
        var server = Server();
        var client = World.CreateClient();
        var clientConn = client.Transport.Connect(World.ServerEndpoint);
        Assert.True(
            World.PumpUntil(() => server.Events.Connected.Count == 1 && client.Events.Connected.Count == 1, _longWait),
            "connection was not established");
        return (server, client, server.Events.Connected[0], clientConn);
    }
}
