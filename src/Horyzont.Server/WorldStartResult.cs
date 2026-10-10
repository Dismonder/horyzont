using Horyzont.Core.Ids;

namespace Horyzont.Server;

/// <summary>Outcome of <see cref="ServerRuntime.StartAsync"/>; expected data problems are results, not exceptions.</summary>
public abstract record WorldStartResult
{
    private protected WorldStartResult()
    {
    }

    /// <summary>Gets a value indicating whether the server listens (normally or read-only).</summary>
    public bool IsRunning => this is Started or StartedReadOnly;

    /// <summary>Gets the process exit code this result stands for when the world did not start (0 when it did).</summary>
    public int ExitCode => this switch
    {
        Started or StartedReadOnly => 0,
        Corrupted or FromNewerVersion => 3,
        ReplayFailed => 4,
        Aborted => 0,
        _ => 70,
    };

    /// <summary>The world loaded, its journal was replayed and the server runs.</summary>
    public sealed record Started : WorldStartResult;

    /// <summary>The world opened in safe mode without writes; it accepts no change until the owner restarts it.</summary>
    /// <param name="Reason">Why.</param>
    /// <param name="Message">Description for the log, the console and the UI.</param>
    public sealed record StartedReadOnly(ReadOnlyReason Reason, string Message) : WorldStartResult;

    /// <summary>The world file is damaged; nothing listens.</summary>
    /// <param name="Message">What failed.</param>
    /// <param name="Backups">Paths of the published backups, oldest first.</param>
    public sealed record Corrupted(string Message, IReadOnlyList<string> Backups) : WorldStartResult;

    /// <summary>The world was written by a newer version of the game; nothing listens.</summary>
    /// <param name="SchemaVersion">Schema version of the file.</param>
    /// <param name="SupportedVersion">Newest schema this build reads.</param>
    public sealed record FromNewerVersion(int SchemaVersion, int SupportedVersion) : WorldStartResult;

    /// <summary>A journal entry could not be replayed; nothing listens.</summary>
    /// <param name="OpId">The operation that failed; <see cref="Horyzont.Core.Ids.OpId.None"/> when no operation is to blame (a terrain area that did not load).</param>
    /// <param name="Message">What failed.</param>
    public sealed record ReplayFailed(OpId OpId, string Message) : WorldStartResult;

    /// <summary>A shutdown was requested while the world was starting; the start was given up and cleaned up, nothing listens.</summary>
    public sealed record Aborted : WorldStartResult;

    /// <summary>The startup checkpoint, or another step of the start, failed; nothing listens.</summary>
    /// <param name="Message">What failed.</param>
    public sealed record StartFailed(string Message) : WorldStartResult;
}
