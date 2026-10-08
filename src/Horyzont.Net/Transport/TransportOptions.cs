namespace Horyzont.Net.Transport;

/// <summary>Liveness settings shared by all transports.</summary>
public sealed record TransportOptions
{
    /// <summary>Initializes a new instance of the <see cref="TransportOptions"/> class.</summary>
    /// <param name="timeout">No traffic from the other side for this long disconnects with <see cref="TransportDisconnectReason.Timeout"/>.</param>
    /// <param name="keepAlive">Interval of keep-alive messages.</param>
    /// <exception cref="ArgumentException">A value is not positive or <paramref name="keepAlive"/> exceeds a quarter of <paramref name="timeout"/>.</exception>
    public TransportOptions(TimeSpan timeout, TimeSpan keepAlive)
    {
        if (timeout <= TimeSpan.Zero || keepAlive <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout and keep-alive must be positive.");
        }

        if (keepAlive > timeout / 4)
        {
            throw new ArgumentException("KeepAlive must not exceed Timeout / 4.", nameof(keepAlive));
        }

        Timeout = timeout;
        KeepAlive = keepAlive;
    }

    /// <summary>Gets ten seconds timeout with one second keep-alive.</summary>
    public static TransportOptions Default { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

    /// <summary>Gets the silence from the other side that ends the connection.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Gets the interval of keep-alive messages.</summary>
    public TimeSpan KeepAlive { get; }
}
