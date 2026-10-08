using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests.Transport;

/// <summary>One server and its clients in a single process, plus control over time and the link.</summary>
public interface ITransportWorld : IDisposable
{
    TransportOptions Options { get; }

    /// <summary>Endpoint clients connect to; valid after <see cref="CreateServer"/>.</summary>
    TransportEndpoint ServerEndpoint { get; }

    TransportNode CreateServer();

    TransportNode CreateClient();

    /// <summary>Applies loss and duplication to unreliable traffic between the server and every client.</summary>
    void SetLoss(double lossRate, double duplicateRate);

    /// <summary>Drops all traffic between the server and every client.</summary>
    void Partition();

    /// <summary>Restores traffic after <see cref="Partition"/>.</summary>
    void Heal();

    /// <summary>Polls all transports repeatedly for <paramref name="duration"/> of (fake or real) time.</summary>
    void Pump(TimeSpan duration);

    /// <summary>Polls until <paramref name="condition"/> holds or <paramref name="max"/> elapses; returns whether it held.</summary>
    bool PumpUntil(Func<bool> condition, TimeSpan max);
}
