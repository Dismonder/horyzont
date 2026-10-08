using Horyzont.Net.Transport;

namespace Horyzont.Net.Session;

/// <summary>What the server knows when it checks a ClientHello.</summary>
/// <param name="Options">Server settings (build hash, password, bans, player limit).</param>
/// <param name="RemoteEndpoint">Remote endpoint reported by the transport.</param>
/// <param name="IdentityConnected">Whether a player with the same platform identity is in the Loading or InGame state.</param>
/// <param name="PlayerCount">Number of players in the Loading and InGame states.</param>
public readonly record struct HandshakeContext(ServerOptions Options, TransportEndpoint RemoteEndpoint, bool IdentityConnected, int PlayerCount);
