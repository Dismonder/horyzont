namespace Horyzont.Client.UI;

/// <summary>Local lobby failure shown by the menu; mirrors the lobby controller of the Steam package.</summary>
public enum UiLobbyError
{
    /// <summary>No error.</summary>
    None,

    /// <summary>The platform is not available.</summary>
    Unavailable,

    /// <summary>The platform did not answer in time.</summary>
    Timeout,

    /// <summary>The lobby is full.</summary>
    Full,

    /// <summary>The lobby refused the player.</summary>
    AccessDenied,

    /// <summary>The lobby data are not valid.</summary>
    InvalidMetadata,

    /// <summary>The host runs another build.</summary>
    IncompatibleBuild,

    /// <summary>The lobby owner changed.</summary>
    OwnerChanged,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,

    /// <summary>Any other platform failure.</summary>
    PlatformFailure,
}
