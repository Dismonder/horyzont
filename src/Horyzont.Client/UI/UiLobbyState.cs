namespace Horyzont.Client.UI;

/// <summary>State of the lobby service as the menu sees it.</summary>
public enum UiLobbyState
{
    /// <summary>No platform.</summary>
    Unavailable,

    /// <summary>Ready, not in a lobby.</summary>
    Idle,

    /// <summary>Creating a lobby.</summary>
    Creating,

    /// <summary>Hosting a lobby.</summary>
    Hosting,

    /// <summary>Joining a lobby.</summary>
    Joining,

    /// <summary>Joined a lobby.</summary>
    Joined,

    /// <summary>Leaving a lobby.</summary>
    Leaving,

    /// <summary>The last operation failed.</summary>
    Failed,
}
