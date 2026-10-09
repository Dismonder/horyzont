using System.Runtime.InteropServices;

using Godot;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Boot;

/// <summary>
/// The single way out of the game: a closed window, SIGINT/SIGTERM/SIGQUIT, the console command <c>stop</c> and the host leaving
/// all end in the same canonical shutdown (ZASADY, point 7), after which the tree quits with the exit code of the process.
/// A signal only sets a flag (signals run on a thread pool thread); the shutdown starts on the main thread in the next frame.
/// The second request during the shutdown is ignored with a log line, the third signal ends the process at once (exit code 130;
/// the journal is durable every 100 ms).
/// </summary>
public sealed partial class QuitCoordinator : Node
{
    private readonly List<PosixSignalRegistration> _registrations = [];
    private Action? _requestShutdown;
    private Func<bool>? _isFinished;
    private Func<int>? _exitCode;
    private Action? _beforeQuit;
    private ILogger<QuitCoordinator>? _logger;
    private bool _registerSignals;
    private int _stopRequested;
    private int _requests;
    private bool _shutdownStarted;
    private bool _quitting;

    /// <summary>Gives the node its dependencies.</summary>
    /// <param name="requestShutdown">Starts the canonical shutdown of the process (server: <c>RequestShutdown</c>; client: disconnect).</param>
    /// <param name="isFinished">True when the shutdown is complete and the process may exit.</param>
    /// <param name="exitCode">Exit code of the process, read when the shutdown is complete.</param>
    /// <param name="beforeQuit">Last cleanup before the tree quits (local client of a host, logs); may be null.</param>
    /// <param name="registerSignals">Whether to react to process signals.</param>
    /// <param name="logger">Logger.</param>
    public void Initialize(
        Action requestShutdown, Func<bool> isFinished, Func<int> exitCode, Action? beforeQuit, bool registerSignals, ILogger<QuitCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(requestShutdown);
        ArgumentNullException.ThrowIfNull(isFinished);
        ArgumentNullException.ThrowIfNull(exitCode);
        ArgumentNullException.ThrowIfNull(logger);
        _requestShutdown = requestShutdown;
        _isFinished = isFinished;
        _exitCode = exitCode;
        _beforeQuit = beforeQuit;
        _registerSignals = registerSignals;
        _logger = logger;
    }

    /// <summary>Asks for the shutdown; safe to call from any thread (it only sets a flag).</summary>
    public void RequestStop() => Volatile.Write(ref _stopRequested, 1);

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_requestShutdown is null)
        {
            throw new InvalidOperationException("QuitCoordinator.Initialize was not called before the node entered the tree.");
        }

        GetTree().AutoAcceptQuit = false;
        if (_registerSignals)
        {
            foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGQUIT })
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, OnSignal));
            }
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            RequestStop();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_quitting)
        {
            return;
        }

        if (Volatile.Read(ref _stopRequested) == 1 && !_shutdownStarted)
        {
            _shutdownStarted = true;
            QuitLog.StopRequested(_logger!);
            _requestShutdown!();
        }

        if (_isFinished!())
        {
            _quitting = true;
            _beforeQuit?.Invoke();
            GetTree().Quit(_exitCode!());
        }
    }

    private void OnSignal(PosixSignalContext context)
    {
        // Runs on a thread pool thread: only flags, the log and, for the third request, the hard exit.
        context.Cancel = true;
        var count = Interlocked.Increment(ref _requests);
        if (count == 1)
        {
            QuitLog.SignalReceived(_logger!, context.Signal);
            RequestStop();
        }
        else if (count == 2)
        {
            QuitLog.SecondRequestIgnored(_logger!, context.Signal);
        }
        else
        {
            System.Environment.Exit(130);
        }
    }
}

internal static partial class QuitLog
{
    [LoggerMessage(EventId = 9001, Level = LogLevel.Information, Message = "stop requested; starting the shutdown")]
    internal static partial void StopRequested(ILogger logger);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Information, Message = "received {Signal}; stopping")]
    internal static partial void SignalReceived(ILogger logger, PosixSignal signal);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "received {Signal} while stopping; ignored (one more ends the process at once)")]
    internal static partial void SecondRequestIgnored(ILogger logger, PosixSignal signal);
}
