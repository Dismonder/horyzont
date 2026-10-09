using System.Runtime.ExceptionServices;

namespace Horyzont.Server;

public sealed partial class ServerRuntime
{
    /// <summary>Builds the canonical shutdown (see ZASADY, point 7). Steps that do not apply to this runtime are marked skipped.</summary>
    private ShutdownSequence BuildShutdown(bool faulted)
    {
        var hasWriter = _writer is not null;
        Task<bool>? checkpoint = null;
        var checkpointStarted = false;

        Task? disposeTransport = null;
        var steps = new List<ShutdownStep>
        {
            new()
            {
                Name = "1 session shutdown",
                Begin = () =>
                {
                    _session!.Shutdown(ShutdownCode);
                    return null;
                },
            },
            new()
            {
                Name = "2 drain session and transport",
                Timeout = _options.DrainTimeout,
                Begin = () => () =>
                {
                    _session!.Poll(_worldState!.CurrentTick);
                    return _session.IsShutdownComplete && _transport.IsDrained;
                },
            },
            new()
            {
                Name = "3a participants shutdown",
                ExceptionFaults = true,
                FailureIsError = true,
                SkipWhenFaulted = true,
                Skip = !hasWriter,
                Begin = () =>
                {
                    foreach (var participant in _participants)
                    {
                        participant.OnShutdown(this);
                    }

                    return null;
                },
            },
            new()
            {
                Name = "3b leave all online players",
                ExceptionFaults = true,
                FailureIsError = true,
                SkipWhenFaulted = true,
                Skip = !hasWriter,
                Begin = () =>
                {
                    _simulation!.LeaveAllOnlinePlayers();
                    return null;
                },
            },
            new()
            {
                Name = "4 checkpoint",
                FailureIsError = true,
                SkipWhenFaulted = true,
                Skip = !hasWriter,
                Timeout = _options.ShutdownCheckpointTimeout,
                TimeoutIsError = true,
                Begin = () => () =>
                {
                    // A checkpoint already in flight finishes first (its event is handled by the tick), then ours starts.
                    if (!checkpointStarted)
                    {
                        if (_worldState!.IsCheckpointInFlight)
                        {
                            return false;
                        }

                        checkpointStarted = true;
                        checkpoint = StartCheckpoint();
                    }

                    return checkpoint!.IsCompleted && !_worldState!.IsCheckpointInFlight;
                },
                Finished = timedOut =>
                {
                    if (!timedOut && checkpointStarted && !ResultOf(checkpoint!))
                    {
                        _shutdown!.MarkError();
                    }
                },
            },
            TaskStep("5 flush journal", () => _writer!.FlushAsync(), _options.FlushTimeout, timeoutIsError: true, skip: !hasWriter, checkResult: true),
            TaskStep("6 dispose writer", () => _writer!.DisposeAsync().AsTask(), _options.DisposeTimeout, timeoutIsError: true, skip: !hasWriter),
            TaskStep("7 shut terrain down", async () => await TerrainShutdownAsync(), _options.TerrainShutdownTimeout, timeoutIsError: false),
            TaskStep(
                "7a shut game systems down", RunAfterTerrainShutdownAsync, _options.TerrainShutdownTimeout, timeoutIsError: false,
                skip: _afterTerrainShutdown.Count == 0),
            new()
            {
                // Off the main thread: LiteNetLib stops its thread with an unbounded join. A transport that hangs is left to the process exit.
                Name = "8 dispose transport",
                Timeout = _options.TransportDisposeTimeout,
                Begin = () =>
                {
                    disposeTransport = Task.Run(_transport.Dispose);
                    return () => disposeTransport.IsCompleted;
                },
                Finished = timedOut =>
                {
                    if (timedOut)
                    {
                        _logger.TransportLeftOpen();
                    }
                    else if (disposeTransport!.IsFaulted)
                    {
                        ExceptionDispatchInfo.Capture(disposeTransport.Exception!.GetBaseException()).Throw();
                    }
                },
            },
        };

        var sequence = new ShutdownSequence(steps, _time, _logger);
        if (faulted)
        {
            sequence.MarkFaulted();
        }

        return sequence;
    }

    private ShutdownStep TaskStep(
        string name, Func<Task> start, TimeSpan timeout, bool timeoutIsError, bool skip = false, bool checkResult = false)
    {
        Task? task = null;
        return new ShutdownStep
        {
            Name = name,
            Skip = skip,
            FailureIsError = timeoutIsError,
            Timeout = timeout,
            TimeoutIsError = timeoutIsError,
            Begin = () =>
            {
                task = start();
                return () => task.IsCompleted;
            },
            Finished = timedOut =>
            {
                if (timedOut)
                {
                    return;
                }

                // The task is completed here, so none of this blocks the main thread.
                if (task!.IsFaulted)
                {
                    ExceptionDispatchInfo.Capture(task.Exception!.GetBaseException()).Throw();
                }

                if (checkResult && task is Task<bool> result && !ResultOf(result))
                {
                    _shutdown!.MarkError();
                }
            },
        };
    }

    private async Task RunAfterTerrainShutdownAsync()
    {
        foreach (var task in _afterTerrainShutdown)
        {
            await task().ConfigureAwait(true);
        }
    }

    private async Task TerrainShutdownAsync()
    {
        if (!await _terrain!.ShutdownAsync().ConfigureAwait(false))
        {
            _logger.TerrainStoreLeftOpen();
        }
    }
}
