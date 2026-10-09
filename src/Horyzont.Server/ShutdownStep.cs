namespace Horyzont.Server;

/// <summary>One step of the <see cref="ShutdownSequence"/>.</summary>
public sealed class ShutdownStep
{
    /// <summary>Gets the name logged when the step starts and recorded when it ends.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the action that starts the step. It returns null when the step is already done, otherwise a function polled once per frame
    /// that returns true when the step is done. Runs on the main thread; an exception counts as a failed step.
    /// </summary>
    public required Func<Func<bool>?> Begin { get; init; }

    /// <summary>Gets the longest time the step may take, measured with the <see cref="TimeProvider"/> of the sequence; infinite by default.</summary>
    public TimeSpan Timeout { get; init; } = System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>Gets a value indicating whether running out of time is an error (exit code 70) and not only a warning.</summary>
    public bool TimeoutIsError { get; init; }

    /// <summary>Gets a value indicating whether an exception in this step marks the sequence faulted, which skips the steps that <see cref="SkipWhenFaulted"/>.</summary>
    public bool ExceptionFaults { get; init; }

    /// <summary>
    /// Gets a value indicating whether an exception in this step is a write error (exit code 70). Steps that do not write the world
    /// (terrain, transport) only log the exception.
    /// </summary>
    public bool FailureIsError { get; init; }

    /// <summary>Gets a value indicating whether the step is skipped once the sequence is faulted.</summary>
    public bool SkipWhenFaulted { get; init; }

    /// <summary>Gets a value indicating whether the step is skipped (it does not apply to this runtime, for example no writer in safe mode).</summary>
    public bool Skip { get; init; }

    /// <summary>Gets the callback that runs when the step is done or out of time; its argument is true on a timeout.</summary>
    public Action<bool>? Finished { get; init; }
}
