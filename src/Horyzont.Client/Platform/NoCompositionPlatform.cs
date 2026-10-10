using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Net.Transport;

namespace Horyzont.Client.Platform;

/// <summary>Neutral K2 platform, preserving existing transports and connection paths.</summary>
public sealed class NoCompositionPlatform : ICompositionPlatform
{
    /// <summary>Gets the stateless default instance.</summary>
    public static NoCompositionPlatform Instance { get; } = new();

    /// <inheritdoc />
    public void ConfigureServerTransports(IList<ITransport> transports, IList<TransportEndpoint> listen)
    {
    }

    /// <inheritdoc />
    public ITransport CreateClientTransport(Func<ITransport> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return fallback();
    }

    /// <inheritdoc />
    public bool TryConnectClient(ClientRuntime client) => false;

    /// <inheritdoc />
    public void OnWorldStarted(WorldId world, int capacity, bool passwordRequired)
    {
    }

    /// <inheritdoc />
    public void OnShutdownRequested()
    {
    }

    /// <inheritdoc />
    public void OnTransportsReleased()
    {
    }
}
