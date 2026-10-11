namespace Horyzont.Client.UI;

/// <summary>A question the player has to answer before the menu acts.</summary>
public enum UiConfirmation
{
    /// <summary>Nothing is pending.</summary>
    None,

    /// <summary>Replace the damaged world file with the newest backup.</summary>
    Restore,

    /// <summary>Discard the changes after the last checkpoint (after a backup).</summary>
    Recover,

    /// <summary>Leave the running world and join a Steam lobby.</summary>
    Invite,
}
