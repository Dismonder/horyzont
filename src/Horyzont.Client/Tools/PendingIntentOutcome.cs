namespace Horyzont.Client.Tools;

/// <summary>How a pending intent ended.</summary>
public enum PendingIntentOutcome : byte
{
    /// <summary>The server applied it.</summary>
    Accepted = 1,

    /// <summary>The server rejected it.</summary>
    Rejected = 2,

    /// <summary>No reply within <see cref="PendingIntentTracker.DeadlineTicks"/>.</summary>
    TimedOut = 3,

    /// <summary>The connection ended first.</summary>
    Disconnected = 4,
}
