using Horyzont.Client.Platform;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Steam;

/// <summary>Plugs Steam P2P transport, lobby and identity into the composition through the platform seams.</summary>
public sealed class SteamSessionBinder : ICompositionPlatform
{
    /// <summary>Initializes a new instance of the <see cref="SteamSessionBinder"/> class.</summary>
    /// <param name="services">Process services.</param>
    /// <param name="options">Session options.</param>
    /// <param name="logger">Logger.</param>
    public SteamSessionBinder(SteamPlatformServices services, SteamSessionOptions options, ILogger<SteamSessionBinder> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        throw new NotImplementedException();
    }

    /// <summary>Gives the binder a way to end the session (a remote client whose join failed, or whose host left the lobby).</summary>
    /// <param name="endSession">Starts the canonical shutdown.</param>
    public void Bind(Action endSession) => throw new NotImplementedException();

    /// <inheritdoc />
    public void ConfigureServerTransports(IList<ITransport> transports, IList<TransportEndpoint> listen) => throw new NotImplementedException();

    /// <inheritdoc />
    public ITransport CreateClientTransport(Func<ITransport> fallback) => throw new NotImplementedException();

    /// <inheritdoc />
    public bool TryConnectClient(ClientRuntime client) => throw new NotImplementedException();

    /// <inheritdoc />
    public void OnWorldStarted(WorldId world, int capacity, bool passwordRequired) => throw new NotImplementedException();

    /// <inheritdoc />
    public void OnShutdownRequested() => throw new NotImplementedException();

    /// <inheritdoc />
    public void OnTransportsReleased() => throw new NotImplementedException();
}
