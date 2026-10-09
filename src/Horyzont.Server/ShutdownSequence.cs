using Microsoft.Extensions.Logging;

namespace Horyzont.Server;

/// <summary>
/// The one canonical shutdown order (decision D8) as explicit steps run on the main thread, one step per frame, each with a deadline
/// measured by a <see cref="TimeProvider"/>. A step that runs out of time is logged and the next one starts; nothing waits for ever.
/// </summary>
public sealed class ShutdownSequence
{
    private readonly IReadOnlyList<ShutdownStep> _steps;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly List<string> _completed = [];
    private int _next;
    private ShutdownStep? _current;
    private Func<bool>? _poll;
    private long _startedAt;

    /// <summary>Initializes a new instance of the <see cref="ShutdownSequence"/> class.</summary>
    /// <param name="steps">The steps in order.</param>
    /// <param name="time">Clock of the deadlines.</param>
    /// <param name="logger">Logger.</param>
    public ShutdownSequence(IReadOnlyList<ShutdownStep> steps, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _steps = steps;
        _time = time;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether every step is done.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Gets a value indicating whether a step failed or ran out of time where that is an error: the process exits with code 70.</summary>
    public bool HadError { get; private set; }

    /// <summary>Gets a value indicating whether the sequence is faulted: the steps that skip a faulted sequence are not run.</summary>
    public bool IsFaulted { get; private set; }

    /// <summary>Gets the names of the steps that are done, in order (steps skipped by choice are not listed).</summary>
    public IReadOnlyList<string> CompletedSteps => _completed;

    /// <summary>Marks the sequence faulted before it starts (a stop after a simulation fault: no leave, no checkpoint).</summary>
    public void MarkFaulted()
    {
        IsFaulted = true;
        HadError = true;
    }

    /// <summary>Records an error found by a step callback (a checkpoint or a flush that returned false).</summary>
    public void MarkError() => HadError = true;

    /// <summary>Starts the next step, or polls the current one. Call once per frame.</summary>
    /// <returns>True when the whole sequence is finished.</returns>
    public bool Step()
    {
        if (IsFinished)
        {
            return true;
        }

        if (_current is null && !BeginNext())
        {
            return IsFinished;
        }

        var step = _current!;
        var done = _poll is null;
        if (!done)
        {
            try
            {
                done = _poll!();
            }
#pragma warning disable CA1031 // A failing step must not stop the shutdown: it is logged, marked and the next step runs.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Fail(step, ex);
                done = true;
            }
        }

        var timedOut = false;
        if (!done && step.Timeout != Timeout.InfiniteTimeSpan && _time.GetElapsedTime(_startedAt) >= step.Timeout)
        {
            _logger.ShutdownStepTimedOut(step.Name, step.Timeout.TotalSeconds);
            HadError |= step.TimeoutIsError;
            timedOut = true;
        }

        if (done || timedOut)
        {
            Complete(step, timedOut);
        }

        return IsFinished;
    }

    private bool BeginNext()
    {
        while (_next < _steps.Count)
        {
            var step = _steps[_next++];
            if (step.Skip || (IsFaulted && step.SkipWhenFaulted))
            {
                continue;
            }

            _logger.ShutdownStep(step.Name);
            _current = step;
            _startedAt = _time.GetTimestamp();
            try
            {
                _poll = step.Begin();
            }
#pragma warning disable CA1031 // A failing step must not stop the shutdown: it is logged, marked and the next step runs.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Fail(step, ex);
                _poll = null;
            }

            return true;
        }

        IsFinished = true;
        return false;
    }

    private void Fail(ShutdownStep step, Exception exception)
    {
        _logger.ShutdownStepFailed(step.Name, exception);
        HadError = true;
        IsFaulted |= step.ExceptionFaults;
    }

    private void Complete(ShutdownStep step, bool timedOut)
    {
        try
        {
            step.Finished?.Invoke(timedOut);
        }
#pragma warning disable CA1031 // The callback only inspects the outcome; a failure there is logged and marked, the sequence goes on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Fail(step, ex);
        }

        _completed.Add(step.Name);
        _current = null;
        _poll = null;
        if (_next >= _steps.Count)
        {
            IsFinished = true;
        }
    }
}
