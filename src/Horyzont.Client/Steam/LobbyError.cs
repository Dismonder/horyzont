namespace Horyzont.Client.Steam;

/// <summary>Local result of a lobby operation; never sent over the wire.</summary>
public enum LobbyError
{
    /// <summary>Success.</summary>
    None,

    /// <summary>Steam is not available.</summary>
    Unavailable,

    /// <summary>The operation did not finish in time.</summary>
    Timeout,

    /// <summary>The lobby is full.</summary>
    Full,

    /// <summary>The lobby refused the player.</summary>
    AccessDenied,

    /// <summary>Metadata is missing, malformed or contradicts the lobby owner.</summary>
    InvalidMetadata,

    /// <summary>The host runs another build or protocol version.</summary>
    IncompatibleBuild,

    /// <summary>The lobby owner changed during the session.</summary>
    OwnerChanged,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,

    /// <summary>The platform reported another failure.</summary>
    PlatformFailure,
}
