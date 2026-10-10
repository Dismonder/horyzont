namespace Horyzont.Client.UI;

/// <summary>Who may see a hosted lobby.</summary>
public enum UiLobbyVisibility
{
    /// <summary>Only invited players.</summary>
    Private,

    /// <summary>Friends of the host.</summary>
    FriendsOnly,

    /// <summary>Everybody.</summary>
    Public,
}
