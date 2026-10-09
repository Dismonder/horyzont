using System.Diagnostics;
using System.Net;

using Horyzont.Net.Transport;
using Horyzont.Net.Transport.Udp;

namespace Horyzont.Net.Tests.Transport;

public sealed class UdpWorld : ITransportWorld, ITransportWorldFactory
{
    private readonly List<TransportNode> _nodes = [];
    private UdpLossyRelay? _relay;

    // The timeout is well above scheduler stalls on loaded CI runners (a 500 ms timeout let a stall kill a healthy connection);
    // tests of the timeout itself derive their waits from it.
    public TransportOptions Options { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));

    public TransportEndpoint ServerEndpoint { get; private set; } = new UdpEndpoint("127.0.0.1", 0);

    public static ITransportWorld Create() => new UdpWorld();

    public TransportNode CreateServer()
    {
        var transport = new UdpTransport(Options);
        transport.Listen(new UdpEndpoint("127.0.0.1", 0));
        _relay = new UdpLossyRelay(new IPEndPoint(IPAddress.Loopback, transport.LocalPort), seed: 99);
        ServerEndpoint = new UdpEndpoint("127.0.0.1", _relay.Port);
        return Track(transport);
    }

    public TransportNode CreateClient() => Track(new UdpTransport(Options));

    public void SetLoss(double lossRate, double duplicateRate)
    {
        _relay!.LossRate = lossRate;
        _relay.DuplicateRate = duplicateRate;
    }

    public void Partition() => _relay!.Partitioned = true;

    public void Heal() => _relay!.Partitioned = false;

    public void Pump(TimeSpan duration) => PumpUntil(() => false, duration);

    public bool PumpUntil(Func<bool> condition, TimeSpan max)
    {
        var watch = Stopwatch.StartNew();
        PollAll();
        while (!condition())
        {
            if (watch.Elapsed >= max)
            {
                return false;
            }

            Thread.Sleep(1);
            PollAll();
        }

        return true;
    }

    public void Dispose()
    {
        foreach (var node in _nodes)
        {
            node.Transport.Dispose();
        }

        _relay?.Dispose();
    }

    private TransportNode Track(ITransport transport)
    {
        var node = new TransportNode(transport, new RecordingEvents());
        _nodes.Add(node);
        return node;
    }

    private void PollAll()
    {
        foreach (var node in _nodes)
        {
            node.Transport.Poll(node.Events);
        }
    }
}
