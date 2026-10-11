namespace Horyzont.Client.UI;

/// <summary>Start result independent of the server runtime.</summary>
public enum UiStartKind
{
    /// <summary>Started state.</summary>
    Started,
    /// <summary>StartedReadOnly state.</summary>
    StartedReadOnly,
    /// <summary>Corrupted state.</summary>
    Corrupted,
    /// <summary>FromNewerVersion state.</summary>
    FromNewerVersion,
    /// <summary>ReplayFailed state.</summary>
    ReplayFailed,
    /// <summary>Aborted state.</summary>
    Aborted,
    /// <summary>StartFailed state.</summary>
    StartFailed,
}
