using Horyzont.Net.Transport;
using Horyzont.Net.Transport.Steam;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Steam;

/// <summary>
/// Process-wide Steam services: one SDK initialization, one lobby controller, one shutdown. Sessions (host, client, the next host
/// after a return to the menu) borrow them; only <see cref="TryShutdown"/> shuts the SDK down.
/// </summary>
public sealed class SteamPlatformServices
{
    private readonly Func<ITransport> _transportFactory;
    private readonly ILogger<SteamPlatformServices> _logger;
    private bool _shutdown;

    private SteamPlatformServices(
        ISteamLobbyApi api, bool available, SteamLobbyController lobby, Func<ITransport> transportFactory, ILogger<SteamPlatformServices> logger)
    {
        Api = api;
        Available = available;
        Lobby = lobby;
        _transportFactory = transportFactory;
        _logger = logger;
    }

    /// <summary>Gets the platform surface.</summary>
    public ISteamLobbyApi Api { get; }

    /// <summary>Gets the lobby controller.</summary>
    public SteamLobbyController Lobby { get; }

    /// <summary>Gets a value indicating whether the SDK initialized.</summary>
    public bool Available { get; }

    /// <summary>Gets or sets a value indicating whether a game session is running.</summary>
    public bool SessionActive { get; set; }

    /// <summary>Initializes the SDK once and builds the services.</summary>
    /// <param name="api">Platform surface.</param>
    /// <param name="appId">Steam application id.</param>
    /// <param name="buildHash">Build hash as decimal text.</param>
    /// <param name="protocolVersion">Protocol version.</param>
    /// <param name="time">Clock.</param>
    /// <param name="loggers">Logger factory.</param>
    /// <param name="transportFactory">Creates the P2P transport; null creates the Steam Networking Sockets transport.</param>
    /// <returns>The services; unavailable when the SDK did not initialize.</returns>
    public static SteamPlatformServices Create(
        ISteamLobbyApi api, uint appId, string buildHash, ushort protocolVersion, TimeProvider time, ILoggerFactory loggers,
        Func<ITransport>? transportFactory = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggers);
        var logger = loggers.CreateLogger<SteamPlatformServices>();
        var available = api.TryInitialize(appId);
        SteamPlatformServices? services = null;
        var lobby = new SteamLobbyController(api, time, () => services!.SessionActive, loggers.CreateLogger<SteamLobbyController>());
        lobby.Configure(buildHash, protocolVersion);
        if (available)
        {
            logger.Initialized(appId, api.LocalSteamId);
        }
        else
        {
            lobby.SetUnavailable();
        }

        services = new SteamPlatformServices(api, available, lobby, transportFactory ?? (() => new SteamTransport(TransportOptions.Default)), logger);
        return services;
    }

    /// <summary>Creates a P2P transport for one session; the composition owns it.</summary>
    /// <returns>The transport.</returns>
    public ITransport CreateTransport() => _transportFactory();

    /// <summary>Runs SDK callbacks and the controller; once per frame.</summary>
    public void Pump()
    {
        if (_shutdown)
        {
            return;
        }

        Api.Pump();
        Lobby.Update();
    }

    /// <summary>Leaves the lobby, drops callbacks and shuts the SDK down; call only after every transport was released.</summary>
    /// <returns>True when finished.</returns>
    public bool TryShutdown()
    {
        if (_shutdown)
        {
            return true;
        }

        _shutdown = true;
        Lobby.Leave();
        Lobby.Dispose();
        if (Available)
        {
            Api.Shutdown();
        }

        _logger.ServicesShutDown();
        return true;
    }
}
