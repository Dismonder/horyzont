namespace Horyzont.Client.Steam;

/// <summary>State of the lobby controller.</summary>
public enum LobbyState
{
    /// <summary>Steam is not available in this process.</summary>
    Unavailable,

    /// <summary>No lobby and no pending operation.</summary>
    Idle,

    /// <summary>A lobby is being created.</summary>
    Creating,

    /// <summary>The local player owns a ready lobby.</summary>
    Hosting,

    /// <summary>A lobby is being joined and checked.</summary>
    Joining,

    /// <summary>The local player joined a valid lobby of another host.</summary>
    Joined,

    /// <summary>The lobby is being left.</summary>
    Leaving,

    /// <summary>The last operation failed; a new one may start.</summary>
    Failed,
}
