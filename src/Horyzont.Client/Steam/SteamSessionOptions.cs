namespace Horyzont.Client.Steam;

/// <summary>What the command line asks of the Steam part of one session.</summary>
public sealed record SteamSessionOptions
{
    /// <summary>Gets the lobby a remote client joins at start (<c>+connect_lobby</c>); null waits for a request from the UI.</summary>
    public ulong? ConnectLobbyId { get; init; }

    /// <summary>Gets a value indicating whether the remote client connects by host and port (UDP), so Steam provides neither transport nor path.</summary>
    public bool ConnectsByAddress { get; init; }

    /// <summary>Gets the visibility of the lobby a host creates.</summary>
    public LobbyVisibility Visibility { get; init; } = LobbyVisibility.FriendsOnly;

    /// <summary>Gets the password a joining client sends (the CLI <c>--password</c>).</summary>
    public string Password { get; init; } = "";
}
