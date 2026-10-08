using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Net.Tests.Transport;

public sealed class InMemoryWorld : ITransportWorld, ITransportWorldFactory
{
    private static readonly TimeSpan _step = TimeSpan.FromMilliseconds(10);

    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryNetwork _network;
    private readonly List<TransportNode> _nodes = [];
    private readonly List<string> _clientNames = [];
    private LinkConditions _conditions = new(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10), 0, 0, 1234);
    private bool _partitioned;

    public InMemoryWorld()
    {
        _network = new InMemoryNetwork(_time, _conditions);
    }

    public TransportOptions Options { get; } = new(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(100));

    public TransportEndpoint ServerEndpoint { get; } = new InMemoryEndpoint("server");

    public InMemoryNetwork Network => _network;

    public static ITransportWorld Create() => new InMemoryWorld();

    public TransportNode CreateServer()
    {
        var transport = _network.CreateTransport("server", Options);
        transport.Listen(ServerEndpoint);
        return Track(transport);
    }

    public TransportNode CreateClient() => CreateClient(null);

    public TransportNode CreateClient(ulong? fakeSteamId)
    {
        var name = "client" + (_clientNames.Count + 1);
        _clientNames.Add(name);
        var transport = _network.CreateTransport(name, Options, fakeSteamId);
        _network.SetConditions("server", name, _conditions);
        if (_partitioned)
        {
            _network.Partition("server", name);
        }

        return Track(transport);
    }

    public void SetLoss(double lossRate, double duplicateRate)
    {
        _conditions = _conditions with { LossRate = lossRate, DuplicateRate = duplicateRate };
        foreach (var name in _clientNames)
        {
            _network.SetConditions("server", name, _conditions);
        }
    }

    public void Partition()
    {
        _partitioned = true;
        foreach (var name in _clientNames)
        {
            _network.Partition("server", name);
        }
    }

    public void Pump(TimeSpan duration) => PumpUntil(() => false, duration);

    public bool PumpUntil(Func<bool> condition, TimeSpan max)
    {
        PollAll();
        var elapsed = TimeSpan.Zero;
        while (!condition())
        {
            if (elapsed >= max)
            {
                return false;
            }

            _time.Advance(_step);
            elapsed += _step;
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
