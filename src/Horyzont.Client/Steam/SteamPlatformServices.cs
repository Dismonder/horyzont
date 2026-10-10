using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Steam;

/// <summary>Process-wide Steam services: one SDK initialization, one lobby controller, one shutdown.</summary>
public sealed class SteamPlatformServices
{
    /// <summary>Gets the platform surface.</summary>
    public ISteamLobbyApi Api => throw new NotImplementedException();

    /// <summary>Gets the lobby controller.</summary>
    public SteamLobbyController Lobby => throw new NotImplementedException();

    /// <summary>Gets a value indicating whether the SDK initialized.</summary>
    public bool Available => throw new NotImplementedException();

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
        Func<ITransport>? transportFactory = null) =>
        throw new NotImplementedException();

    /// <summary>Creates a P2P transport for one session; the composition owns it.</summary>
    /// <returns>The transport.</returns>
    public ITransport CreateTransport() => throw new NotImplementedException();

    /// <summary>Runs SDK callbacks and the controller; once per frame.</summary>
    public void Pump() => throw new NotImplementedException();

    /// <summary>Leaves the lobby, drops callbacks and shuts the SDK down; call only after every transport was released.</summary>
    /// <returns>True when finished.</returns>
    public bool TryShutdown() => throw new NotImplementedException();
}
