using System.Buffers.Binary;

using Horyzont.Net.Transport;
using Horyzont.Net.Transport.Steam;

using Steamworks;

namespace Horyzont.Net.SteamCheck;

/// <summary>Both roles of the manual scenario: the host answers, the joiner drives and reports.</summary>
internal sealed class CheckPeer(SteamTransport transport) : ITransportEvents
{
    private const byte TagSequence = 1;
    private const byte TagBig = 2;
    private const byte TagUnreliable = 3;
    private const byte TagStart = 0x10;
    private const byte TagReportRequest = 0x11;
    private const byte TagReport = 0x12;
    private const int SequenceCount = 1000;
    private const int BigSize = 256 * 1024;
    private const int UnreliableCount = 1000;
    private const int UnreliableSize = 1150;

    private ConnectionId _connection;
    private bool _connected;
    private TransportDisconnectReason? _lastDisconnect;
    private bool _hostSeesJoiner;
    private int _sequenceNext;
    private int _sequenceReceived;
    private bool _sequenceOk = true;
    private bool _bigOk;
    private int _unreliableReceived;
    private int _unreliableBad;
    private int _unreliableToSend;
    private bool _remoteEndpointOk;
    private byte[]? _report;

    public int RunHost()
    {
        transport.Listen(new SteamEndpoint(0));
        Console.WriteLine($"Hosting. My SteamID: {SteamUser.GetSteamID().m_SteamID}");
        while (true)
        {
            Pump();
            Thread.Sleep(10);
        }
    }

    public int RunJoin(ulong hostId)
    {
        var failures = 0;
        bool Step(string name, bool pass, string detail = "")
        {
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name} {detail}".TrimEnd());
            failures += pass ? 0 : 1;
            return pass;
        }

        var myId = SteamUser.GetSteamID().m_SteamID;
        _connection = transport.Connect(new SteamEndpoint(hostId));
        if (!Step("connect", WaitUntil(() => _connected, TimeSpan.FromSeconds(30))))
        {
            return 1;
        }

        Step("remote-endpoint-joiner", transport.GetRemoteEndpoint(_connection) == new SteamEndpoint(hostId));

        var start = new byte[9];
        start[0] = TagStart;
        BinaryPrimitives.WriteUInt64LittleEndian(start.AsSpan(1), myId);
        transport.Send(_connection, TransportChannel.ReliableOrdered, start);
        BeginStreams();
        WaitUntil(() => _sequenceReceived >= SequenceCount && _bigOk && _unreliableToSend == 0, TimeSpan.FromSeconds(30));
        WaitFor(TimeSpan.FromSeconds(3));
        Step("reliable-host-to-join", _sequenceOk && _sequenceReceived == SequenceCount, $"received={_sequenceReceived}");
        Step("big-256KiB-host-to-join", _bigOk);
        Step("unreliable-host-to-join", _unreliableReceived > 0 && _unreliableBad == 0, $"delivered={_unreliableReceived}/{UnreliableCount} corrupted={_unreliableBad}");

        transport.Send(_connection, TransportChannel.ReliableOrdered, [TagReportRequest]);
        if (Step("report", WaitUntil(() => _report is not null, TimeSpan.FromSeconds(15))))
        {
            var r = _report!;
            var received = BinaryPrimitives.ReadInt32LittleEndian(r.AsSpan(1));
            Step("reliable-join-to-host", r[5] == 1 && received == SequenceCount, $"received={received}");
            Step("big-256KiB-join-to-host", r[6] == 1);
            var delivered = BinaryPrimitives.ReadInt32LittleEndian(r.AsSpan(7));
            var bad = BinaryPrimitives.ReadInt32LittleEndian(r.AsSpan(11));
            Step("unreliable-join-to-host", delivered > 0 && bad == 0, $"delivered={delivered}/{UnreliableCount} corrupted={bad}");
            Step("remote-endpoint-host", r[15] == 1);
        }

        var first = _connection;
        _lastDisconnect = null;
        transport.Disconnect(_connection);
        Step("disconnect-local", WaitUntil(() => _lastDisconnect == TransportDisconnectReason.Local, TimeSpan.FromSeconds(5)), "(the host prints disconnect-remote)");

        _connected = false;
        _connection = transport.Connect(new SteamEndpoint(hostId));
        Step("reconnect", WaitUntil(() => _connected, TimeSpan.FromSeconds(30)) && _connection != first);

        Console.WriteLine("Now kill the host process abruptly (taskkill /F or Task Manager); waiting for Timeout...");
        _lastDisconnect = null;
        var seen = WaitUntil(() => _lastDisconnect is not null, TimeSpan.FromMinutes(2));
        Step("host-killed-timeout", seen && _lastDisconnect == TransportDisconnectReason.Timeout, $"reason={_lastDisconnect}");
        Console.WriteLine(failures == 0 ? "RESULT PASS" : $"RESULT FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }

    public void OnConnected(ConnectionId connection)
    {
        _connection = connection;
        _connected = true;
        _sequenceNext = 0;
        _sequenceReceived = 0;
        _sequenceOk = true;
        _bigOk = false;
        _unreliableReceived = 0;
        _unreliableBad = 0;
        Console.WriteLine($"event connected {connection.Value} remote={transport.GetRemoteEndpoint(connection)}");
    }

    public void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason)
    {
        _connected = false;
        _lastDisconnect = reason;
        Console.WriteLine($"event disconnected {connection.Value} reason={reason}");
        if (_hostSeesJoiner)
        {
            _hostSeesJoiner = false;
            Console.WriteLine($"{(reason == TransportDisconnectReason.Remote ? "PASS" : "INFO")} disconnect-remote reason={reason}");
        }
    }

    public void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        switch (payload[0])
        {
            case TagSequence:
                var index = BinaryPrimitives.ReadInt32LittleEndian(payload[1..]);
                _sequenceOk &= index == _sequenceNext;
                _sequenceNext = index + 1;
                _sequenceReceived++;
                break;
            case TagBig:
                _bigOk = payload.Length == BigSize && payload[1..].SequenceEqual(Pattern(BigSize - 1, 7));
                break;
            case TagUnreliable:
                _unreliableReceived++;
                var seed = BinaryPrimitives.ReadInt32LittleEndian(payload[1..]);
                if (payload.Length != UnreliableSize || !payload[5..].SequenceEqual(Pattern(UnreliableSize - 5, seed)))
                {
                    _unreliableBad++;
                }

                break;
            case TagStart:
                var joinerId = BinaryPrimitives.ReadUInt64LittleEndian(payload[1..]);
                _remoteEndpointOk = transport.GetRemoteEndpoint(connection) == new SteamEndpoint(joinerId);
                _hostSeesJoiner = true;
                _connection = connection;
                Console.WriteLine($"joiner {joinerId} remote-endpoint-host {(_remoteEndpointOk ? "PASS" : "FAIL")}");
                BeginStreams();
                break;
            case TagReportRequest:
                var report = new byte[16];
                report[0] = TagReport;
                BinaryPrimitives.WriteInt32LittleEndian(report.AsSpan(1), _sequenceReceived);
                report[5] = _sequenceOk ? (byte)1 : (byte)0;
                report[6] = _bigOk ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteInt32LittleEndian(report.AsSpan(7), _unreliableReceived);
                BinaryPrimitives.WriteInt32LittleEndian(report.AsSpan(11), _unreliableBad);
                report[15] = _remoteEndpointOk ? (byte)1 : (byte)0;
                transport.Send(connection, TransportChannel.ReliableOrdered, report);
                break;
            case TagReport:
                _report = payload.ToArray();
                break;
            default:
                break;
        }
    }

    private static byte[] Pattern(int length, int seed)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(seed + (i * 31));
        }

        return data;
    }

    private void BeginStreams()
    {
        for (var i = 0; i < SequenceCount; i++)
        {
            var message = new byte[37];
            message[0] = TagSequence;
            BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(1), i);
            transport.Send(_connection, TransportChannel.ReliableOrdered, message);
        }

        var big = new byte[BigSize];
        big[0] = TagBig;
        Pattern(BigSize - 1, 7).CopyTo(big.AsSpan(1));
        transport.Send(_connection, TransportChannel.ReliableOrdered, big);
        _unreliableToSend = UnreliableCount;
    }

    private void Pump()
    {
        SteamAPI.RunCallbacks();
        transport.Poll(this);
        for (var i = 0; i < 20 && _unreliableToSend > 0 && _connected; i++)
        {
            var seed = UnreliableCount - _unreliableToSend;
            var message = new byte[UnreliableSize];
            message[0] = TagUnreliable;
            BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(1), seed);
            Pattern(UnreliableSize - 5, seed).CopyTo(message.AsSpan(5));
            transport.Send(_connection, TransportChannel.Unreliable, message);
            _unreliableToSend--;
        }
    }

    private bool WaitUntil(Func<bool> condition, TimeSpan max)
    {
        var deadline = DateTime.UtcNow + max;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            Pump();
            Thread.Sleep(10);
        }

        return true;
    }

    private void WaitFor(TimeSpan duration) => WaitUntil(() => false, duration);
}
