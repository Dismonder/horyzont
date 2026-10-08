using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Horyzont.Net.Tests.Transport;

/// <summary>
/// UDP relay between clients and one server that drops, duplicates and delays datagrams (seeded), or drops everything.
/// Every client address gets its own socket towards the server, so the server sees distinct peers.
/// </summary>
public sealed class UdpLossyRelay : IDisposable
{
    private const int SioUdpConnReset = -1744830452;

    private readonly Socket _front;
    private readonly IPEndPoint _server;
    private readonly Thread _thread;
    private readonly Random _random;
    private readonly Dictionary<IPEndPoint, Back> _backByClient = [];
    private readonly List<Back> _backs = [];
    private readonly PriorityQueue<Pending, (long Due, long Seq)> _delayed = new();
    private readonly byte[] _buffer = new byte[2048];
    private readonly List<Socket> _readable = [];
    private long _seq;
    private volatile bool _stop;
    private volatile Settings _settings = new(0, 0, TimeSpan.Zero, TimeSpan.Zero, false);

    public UdpLossyRelay(IPEndPoint server, int seed)
    {
        _server = server;
        _random = new Random(seed);
        _front = CreateSocket();
        Port = ((IPEndPoint)_front.LocalEndPoint!).Port;
        _thread = new Thread(Run) { IsBackground = true, Name = "UdpLossyRelay" };
        _thread.Start();
    }

    private sealed record Back(Socket Socket, IPEndPoint Client);

    private sealed record Pending(Socket Socket, IPEndPoint Destination, byte[] Data);

    private sealed record Settings(double Loss, double Duplicate, TimeSpan Latency, TimeSpan Jitter, bool Partitioned);

    /// <summary>Gets the UDP port clients connect to.</summary>
    public int Port { get; }

    public double LossRate
    {
        get => _settings.Loss;
        set => _settings = _settings with { Loss = value };
    }

    public double DuplicateRate
    {
        get => _settings.Duplicate;
        set => _settings = _settings with { Duplicate = value };
    }

    public bool Partitioned
    {
        get => _settings.Partitioned;
        set => _settings = _settings with { Partitioned = value };
    }

    public void SetDelay(TimeSpan latency, TimeSpan jitter) =>
        _settings = _settings with { Latency = latency, Jitter = jitter };

    public void Dispose()
    {
        _stop = true;
        _thread.Join();
        _front.Dispose();
        foreach (var back in _backs)
        {
            back.Socket.Dispose();
        }
    }

    private static Socket CreateSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.ReceiveBufferSize = 1 << 20;
        if (OperatingSystem.IsWindows())
        {
            // Stop Windows from turning ICMP "port unreachable" into receive errors.
            socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }

        return socket;
    }

    private void Run()
    {
        while (!_stop)
        {
            _readable.Clear();
            _readable.Add(_front);
            foreach (var back in _backs)
            {
                _readable.Add(back.Socket);
            }

            Socket.Select(_readable, null, null, 1000);
            foreach (var socket in _readable)
            {
                Drain(socket);
            }

            FlushDue();
        }
    }

    private void Drain(Socket socket)
    {
        while (socket.Available > 0)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int length;
            try
            {
                length = socket.ReceiveFrom(_buffer, ref from);
            }
            catch (SocketException)
            {
                // Test infrastructure: a transient receive error just loses the datagram, like the network would.
                continue;
            }

            var data = _buffer.AsSpan(0, length).ToArray();
            if (socket == _front)
            {
                var client = (IPEndPoint)from;
                if (!_backByClient.TryGetValue(client, out var back))
                {
                    back = new Back(CreateSocket(), client);
                    _backByClient.Add(client, back);
                    _backs.Add(back);
                }

                Schedule(new Pending(back.Socket, _server, data));
            }
            else
            {
                var back = _backs.Find(b => b.Socket == socket)!;
                Schedule(new Pending(_front, back.Client, data));
            }
        }
    }

    private void Schedule(Pending pending)
    {
        var s = _settings;
        if (s.Partitioned || _random.NextDouble() < s.Loss)
        {
            return;
        }

        Enqueue(pending, s);
        if (_random.NextDouble() < s.Duplicate)
        {
            Enqueue(pending, s);
        }
    }

    private void Enqueue(Pending pending, Settings s)
    {
        var delay = s.Latency + (s.Jitter * _random.NextDouble());
        var due = Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency);
        _delayed.Enqueue(pending, (due, _seq++));
    }

    private void FlushDue()
    {
        var now = Stopwatch.GetTimestamp();
        while (_delayed.TryPeek(out var pending, out var key) && key.Due <= now)
        {
            _delayed.Dequeue();
            try
            {
                pending.Socket.SendTo(pending.Data, pending.Destination);
            }
            catch (SocketException)
            {
                // Peer socket already closed during shutdown; losing the datagram is the correct simulation.
            }
        }
    }
}
