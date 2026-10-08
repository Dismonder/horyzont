namespace Horyzont.Net.Transport;

/// <summary>Address of a Steam peer. For clients it is the SteamID of the host; servers ignore it.</summary>
/// <param name="SteamId">64-bit SteamID.</param>
public sealed record SteamEndpoint(ulong SteamId) : TransportEndpoint;
