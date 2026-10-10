using Horyzont.Client.Platform;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Steam;

/// <summary>
/// Plugs Steam P2P transport, lobby and identity into the composition through the platform seams of one session. A host adds the
/// P2P transport and publishes a lobby once the world runs; a remote client joins a lobby and connects to its owner. The local
/// client of a host never passes through here: it keeps the Dev identity over the in-memory transport.
/// </summary>
public sealed class SteamSessionBinder : ICompositionPlatform
{
    private readonly SteamPlatformServices _services;
    private readonly SteamSessionOptions _options;
    private readonly ILogger<SteamSessionBinder> _logger;
    private ClientRuntime? _client;
    private Action? _endSession;
    private bool _clientRole;
    private bool _subscribed;

    /// <summary>Initializes a new instance of the <see cref="SteamSessionBinder"/> class.</summary>
    /// <param name="services">Process services.</param>
    /// <param name="options">Session options.</param>
    /// <param name="logger">Logger.</param>
    public SteamSessionBinder(SteamPlatformServices services, SteamSessionOptions options, ILogger<SteamSessionBinder> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <summary>Gives the binder a way to end the session (a remote client whose join failed, or whose host left the lobby).</summary>
    /// <param name="endSession">Starts the canonical shutdown.</param>
    public void Bind(Action endSession)
    {
        ArgumentNullException.ThrowIfNull(endSession);
        _endSession = endSession;
    }

    /// <inheritdoc />
    public void ConfigureServerTransports(IList<ITransport> transports, IList<TransportEndpoint> listen)
    {
        ArgumentNullException.ThrowIfNull(transports);
        ArgumentNullException.ThrowIfNull(listen);
        _services.SessionActive = true;
        if (!_services.Available)
        {
            return;
        }

        transports.Add(_services.CreateTransport());
        listen.Add(new SteamEndpoint(_services.Api.LocalSteamId));
    }

    /// <inheritdoc />
    public ITransport CreateClientTransport(Func<ITransport> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        _services.SessionActive = true;
        if (_options.ConnectsByAddress)
        {
            return fallback();
        }

        if (!_services.Available)
        {
            throw new InvalidOperationException("Steam is not available, so a Steam lobby cannot be joined.");
        }

        _clientRole = true;
        return _services.CreateTransport();
    }

    /// <inheritdoc />
    public bool TryConnectClient(ClientRuntime client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (_options.ConnectsByAddress || !_services.Available)
        {
            return false;
        }

        _client = client;
        Subscribe();
        if (_options.ConnectLobbyId is { } lobbyId)
        {
            _services.Lobby.Join(lobbyId);
        }

        return true;
    }

    /// <inheritdoc />
    public void OnWorldStarted(WorldId world, int capacity, bool passwordRequired)
    {
        if (!_services.Available)
        {
            return;
        }

        var lobby = _services.Lobby;
        lobby.SetSession(new LobbySessionInfo(world.Value.ToString("D"), passwordRequired));
        lobby.Host(_options.Visibility, capacity);
    }

    /// <inheritdoc />
    public void OnShutdownRequested()
    {
        Unsubscribe();
        if (_services.Available)
        {
            _services.Lobby.PrepareShutdown();
        }
    }

    /// <inheritdoc />
    public void OnTransportsReleased()
    {
        Unsubscribe();
        _client = null;
        if (_services.Available)
        {
            _services.Lobby.Leave();
        }

        _services.SessionActive = false;
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _services.Lobby.ConnectRequested += OnConnectRequested;
        _services.Lobby.Failed += OnLobbyFailed;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        _services.Lobby.ConnectRequested -= OnConnectRequested;
        _services.Lobby.Failed -= OnLobbyFailed;
    }

    private void OnConnectRequested(SteamEndpoint host, bool passwordRequired)
    {
        if (_client is null)
        {
            return;
        }

        if (passwordRequired && _options.Password.Length == 0)
        {
            // The connection still goes ahead: the server answers BadPassword, which is the documented CLI outcome.
            _logger.PasswordMissing(host.SteamId);
        }

        var api = _services.Api;
        _client.Connect(host, new PlatformIdentity(IdentityPlatform.Steam, api.LocalSteamId), api.DisplayName, _options.Password);
    }

    private void OnLobbyFailed(LobbyError error)
    {
        // A remote client has nothing to wait for once its join failed or its host left the lobby; a host keeps its LAN world.
        if (_clientRole)
        {
            _logger.ClientSessionEnded(error);
            _endSession?.Invoke();
        }
    }
}
