using System.Buffers.Binary;

using Horyzont.Bots.Transport;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Bots.Tests;

public sealed class LatencyTransportTests
{
    private static readonly TimeSpan _oneWay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _jitter = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void LatencyTransport_OneWay50Jitter10_DeliversInOrderWithinBounds()
    {
        var time = new FakeTimeProvider();
        var network = new InMemoryNetwork(time);
        using var server = network.CreateTransport("server", TransportOptions.Default);
        server.Listen(new InMemoryEndpoint("server"));
        using var client = new LatencyTransport(network.CreateTransport("client", TransportOptions.Default), time, _oneWay, _jitter, seed: 7);
        var serverEvents = new Recorder(time);
        var clientEvents = new Recorder(time);
        var connection = client.Connect(new InMemoryEndpoint("server"));

        var sent = new List<(int Index, long At)>();
        var next = 0;
        for (var ms = 0; ms < 2000; ms++)
        {
            if (clientEvents.Connected > 0 && serverEvents.Connected > 0 && ms % 3 == 0 && next < 200)
            {
                var payload = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(payload, next);
                client.Send(connection, TransportChannel.ReliableOrdered, payload);
                sent.Add((next++, time.GetTimestamp()));
            }

            client.Poll(clientEvents);
            server.Poll(serverEvents);
            time.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(200, serverEvents.Received.Count);
        Assert.Equal(Enumerable.Range(0, 200), serverEvents.Received.Select(r => r.Index));
        foreach (var (index, arrival) in serverEvents.Received)
        {
            var delay = time.GetElapsedTime(sent[index].At, arrival).TotalMilliseconds;
            Assert.InRange(delay, 40.0, 61.0);
        }

        // Both directions are delayed: the connect confirmation reaches the client after the server saw the request.
        Assert.Equal(1, clientEvents.Connected);
    }

    [Fact]
    public void LatencyTransport_UnreliableMessages_AreDelayedToo()
    {
        var time = new FakeTimeProvider();
        var network = new InMemoryNetwork(time);
        using var server = network.CreateTransport("server", TransportOptions.Default);
        server.Listen(new InMemoryEndpoint("server"));
        using var client = new LatencyTransport(network.CreateTransport("client", TransportOptions.Default), time, _oneWay, TimeSpan.Zero, seed: 1);
        var serverEvents = new Recorder(time);
        var clientEvents = new Recorder(time);
        var connection = client.Connect(new InMemoryEndpoint("server"));
        for (var ms = 0; ms < 400 && !(clientEvents.Connected > 0 && serverEvents.Connected > 0); ms++)
        {
            client.Poll(clientEvents);
            server.Poll(serverEvents);
            time.Advance(TimeSpan.FromMilliseconds(1));
        }

        var start = time.GetTimestamp();
        client.Send(connection, TransportChannel.Unreliable, new byte[4]);
        var arrived = false;
        for (var ms = 0; ms < 200 && !arrived; ms++)
        {
            client.Poll(clientEvents);
            server.Poll(serverEvents);
            arrived = serverEvents.Received.Count == 1;
            if (!arrived)
            {
                time.Advance(TimeSpan.FromMilliseconds(1));
            }
        }

        Assert.True(arrived);
        Assert.InRange(time.GetElapsedTime(start).TotalMilliseconds, 50.0, 52.0);
    }

    private sealed class Recorder(TimeProvider time) : ITransportEvents
    {
        public int Connected { get; private set; }

        public List<(int Index, long At)> Received { get; } = [];

        public void OnConnected(ConnectionId connection) => Connected++;

        public void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason)
        {
        }

        public void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) =>
            Received.Add((BinaryPrimitives.ReadInt32LittleEndian(payload), time.GetTimestamp()));
    }
}
