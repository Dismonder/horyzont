namespace Horyzont.Client.Steam;

/// <summary>What the controller knows about the active lobby.</summary>
/// <param name="LobbyId">Lobby id (not an endpoint).</param>
/// <param name="HostSteamId">SteamID of the host, the P2P endpoint.</param>
/// <param name="Visibility">Visibility; the local setting when hosting, private (unknown) when joined.</param>
/// <param name="Members">Number of members at the time of the last change.</param>
/// <param name="Capacity">Player capacity published by the host.</param>
/// <param name="BuildHash">Build hash published by the host.</param>
/// <param name="ProtocolVersion">Protocol version published by the host.</param>
public readonly record struct LobbyInfo(
    ulong LobbyId, ulong HostSteamId, LobbyVisibility Visibility, int Members, int Capacity, string BuildHash, ushort ProtocolVersion);
