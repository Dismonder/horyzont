namespace Horyzont.Client.UI;

/// <summary>What the menu starts once the previous session has been released.</summary>
public enum UiRelaunchKind
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Start the same world again with the newest backup restored first.</summary>
    RestoreLatest,

    /// <summary>Start the same world again from its last checkpoint, dropping the journal tail.</summary>
    RecoverFromCheckpoint,

    /// <summary>Join the Steam lobby of <see cref="MenuController.PendingLobbyId"/>.</summary>
    ConnectLobby,
}
