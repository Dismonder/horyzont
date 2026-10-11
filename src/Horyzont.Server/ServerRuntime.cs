using System.Globalization;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Persistence.Writing;
using Horyzont.Server.Ships;
using Horyzont.Server.UI;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server;

/// <summary>
/// The server of one process (solo, host or dedicated): starts a world from its file, runs the tick on the main thread, writes
/// checkpoints, reacts to write errors with a safe mode and stops in the canonical order of the <see cref="ShutdownSequence"/>.
/// Everything here runs on the main thread of the game; the writer thread is reached only through its queues.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The writer is disposed by step 6 of the ShutdownSequence (or by the cleanup of a failed start), in the canonical order.")]
public sealed partial class ServerRuntime
{
    private readonly ProcessMode _mode;
    private readonly ServerRuntimeOptions _options;
    private readonly ITransport _transport;
    private readonly IServerTerrainFactory _terrainFactory;
    private readonly Func<WorldState> _createNewWorld;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<ServerRuntime> _logger;
    private readonly List<IServerTickParticipant> _participants = [];
    private readonly List<IServerPlayerObserver> _playerObservers = [];
    private readonly List<IServerClientMessageHandler> _clientMessageHandlers = [];
    private readonly List<Func<Task>> _afterTerrainShutdown = [];

    private WorldState? _worldState;
    private ServerSimulation? _simulation;
    private ShipStatsCache? _shipStats;
    private ServerSession? _session;
    private IServerTerrain? _terrain;
    private PersistenceWriter? _writer;
    private ShutdownSequence? _shutdown;
    private bool _readOnlyStart;
    private bool _saveRequested;
    private bool _clearRequested;
    private bool _shutdownRequestedWhileStarting;
    private CancellationTokenSource? _startCts;
    private long _lastCheckpointAt;

    /// <summary>Initializes a new instance of the <see cref="ServerRuntime"/> class.</summary>
    /// <param name="mode">The mode of the process; <see cref="ProcessMode.Client"/> has no server.</param>
    /// <param name="options">Settings.</param>
    /// <param name="transport">The listening transport; the runtime disposes it in the last step of the shutdown (the caller disposes it when the world did not start).</param>
    /// <param name="terrainFactory">Creates the terrain from the loaded world.</param>
    /// <param name="createNewWorld">Creates the state of a new world; called only when the world file does not exist.</param>
    /// <param name="time">Clock of the checkpoint interval and the shutdown deadlines.</param>
    /// <param name="loggers">Loggers.</param>
    public ServerRuntime(
        ProcessMode mode, ServerRuntimeOptions options, ITransport transport, IServerTerrainFactory terrainFactory,
        Func<WorldState> createNewWorld, TimeProvider time, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(terrainFactory);
        ArgumentNullException.ThrowIfNull(createNewWorld);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggers);
        if (mode == ProcessMode.Client)
        {
            throw new ArgumentException("A client process has no server.", nameof(mode));
        }

        _mode = mode;
        _options = options;
        _transport = transport;
        _terrainFactory = terrainFactory;
        _createNewWorld = createNewWorld;
        _time = time;
        _loggers = loggers;
        _logger = loggers.CreateLogger<ServerRuntime>();
        var status = new ServerStatusBroadcaster(() => CurrentStatus, (player, message) => Session.Send(player, message));
        AddTickParticipant(status);
        AddPlayerObserver(status);
    }

    /// <summary>Gets the life cycle state.</summary>
    public ServerRuntimeState RuntimeState { get; private set; }

    /// <summary>Gets the public availability status, without private diagnostic data.</summary>
    public ServerStatusInfo CurrentStatus => _readOnlyStart
        ? new(true, 1, "Terrain semantics mismatch")
        : RuntimeState switch
        {
            ServerRuntimeState.Running => new(false, 0, "Running"),
            ServerRuntimeState.SafeMode => new(true, 2, "Writer unavailable"),
            _ => new(true, 3, "Not running"),
        };

    /// <summary>Gets the process exit code: 0, or 70 after a fault or a failed shutdown step (3 and 4 come from <see cref="WorldStartResult.ExitCode"/>).</summary>
    public int ExitCode { get; private set; }

    /// <summary>Gets a value indicating whether the shutdown is over (also after a fault) and the process may exit.</summary>
    public bool IsStopped => RuntimeState == ServerRuntimeState.Stopped || (RuntimeState == ServerRuntimeState.Faulted && _shutdown is { IsFinished: true });

    /// <summary>Gets the world state after <see cref="StartAsync"/>; main thread, read-only outside the hooks.</summary>
    /// <exception cref="InvalidOperationException">The world was not started.</exception>
    public WorldState State => _worldState ?? throw new InvalidOperationException("The server has not loaded a world.");

    /// <summary>Gets the simulation after <see cref="StartAsync"/>.</summary>
    /// <exception cref="InvalidOperationException">The world was not started.</exception>
    public ServerSimulation Simulation => _simulation ?? throw new InvalidOperationException("The server has not loaded a world.");

    /// <summary>Gets the stats of the constructs in the ship mode (K5-23).</summary>
    public ShipStatsCache ShipStats => _shipStats ?? throw new InvalidOperationException("The server has not loaded a world.");

    /// <summary>Gets the session after <see cref="StartAsync"/>.</summary>
    /// <exception cref="InvalidOperationException">The world was not started.</exception>
    public ServerSession Session => _session ?? throw new InvalidOperationException("The server is not listening.");

    /// <summary>Gets the names of the shutdown steps that are done, in order (diagnostics and tests).</summary>
    public IReadOnlyList<string> CompletedShutdownSteps => _shutdown?.CompletedSteps ?? [];

    /// <summary>Gets a value indicating whether the world runs in safe mode without a writer (a journal tail that was not replayed).</summary>
    public bool IsReadOnlyStart => _readOnlyStart;

    /// <summary>Gets the tick participants in run order (tests).</summary>
    internal IReadOnlyList<IServerTickParticipant> TickParticipants => _participants;

    /// <summary>Registers a system that takes part in every tick: by <see cref="IServerTickParticipant.Phase"/>, in registration order within a phase.</summary>
    /// <param name="participant">The participant.</param>
    public void AddTickParticipant(IServerTickParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        // Stable insertion: after every participant of the same or an earlier phase.
        var phase = participant.Phase;
        var at = _participants.Count;
        while (at > 0 && _participants[at - 1].Phase > phase)
        {
            at--;
        }

        _participants.Insert(at, participant);
    }

    /// <summary>Finds the first registered tick participant of a type (console commands of systems registered by the composition).</summary>
    /// <typeparam name="T">The participant type.</typeparam>
    /// <param name="participant">Receives the participant.</param>
    /// <returns>False when none is registered.</returns>
    public bool TryGetTickParticipant<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? participant)
        where T : class, IServerTickParticipant
    {
        foreach (var item in _participants)
        {
            if (item is T match)
            {
                participant = match;
                return true;
            }
        }

        participant = null;
        return false;
    }

    /// <summary>Registers a client message handler on the main thread before start.</summary>
    /// <param name="handler">Handler called in registration order, without short circuiting.</param>
    /// <exception cref="InvalidOperationException">Start has already begun.</exception>
    public void AddClientMessageHandler(IServerClientMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (RuntimeState != ServerRuntimeState.Created)
        {
            throw new InvalidOperationException("Client message handlers must be registered before start.");
        }

        _clientMessageHandlers.Add(handler);
    }

    /// <summary>Registers a system told when players are ready and before they leave; call before <see cref="StartAsync"/>.</summary>
    /// <param name="observer">The observer.</param>
    public void AddPlayerObserver(IServerPlayerObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _playerObservers.Add(observer);
    }

    /// <summary>
    /// Registers a task of step 7a of the shutdown, which runs after the terrain is shut down and before the transport is disposed
    /// (K2-13: the construct world of a host). The tasks run one after another in registration order, each started on the main thread;
    /// every task runs even if an earlier one throws, and each failure is logged. A failure (or a timeout) of the step is only
    /// logged, like the terrain step: it does not change the exit code. Call before <see cref="StartAsync"/>.
    /// </summary>
    /// <param name="task">The shutdown work; it must not touch the scene tree off the main thread.</param>
    public void AddAfterTerrainShutdown(Func<Task> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _afterTerrainShutdown.Add(task);
    }

    /// <summary>Tells whether a player is the local player of a host (connected through <see cref="ServerRuntimeOptions.LocalClientEndpoint"/>).</summary>
    /// <param name="player">The player.</param>
    /// <returns>True for the local player of a host; false for remote players, unknown players and dedicated servers.</returns>
    public bool IsLocalPlayer(PlayerId player) =>
        _options.LocalClientEndpoint is { } local && _session?.GetEndpoint(player) is { } endpoint && endpoint == local;

    /// <summary>Asks for a checkpoint now (or after the one in flight); in safe mode after a write error it first clears the writer fault.</summary>
    public void RequestCheckpoint()
    {
        if (RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode) || _writer is null)
        {
            return;
        }

        _saveRequested = true;
        if (RuntimeState == ServerRuntimeState.SafeMode)
        {
            _clearRequested = true;
            _writer.ClearFault();
        }
    }

    /// <summary>Asks for the shutdown (console stop, window close, signal, the host leaving); a second request while stopping is ignored.</summary>
    public void RequestShutdown()
    {
        switch (RuntimeState)
        {
            case ServerRuntimeState.Starting:
                _shutdownRequestedWhileStarting = true;
                _startCts?.Cancel();
                break;
            case ServerRuntimeState.Running or ServerRuntimeState.SafeMode:
                BeginShutdown(faulted: false);
                break;
            case ServerRuntimeState.ShuttingDown or ServerRuntimeState.Faulted:
                _logger.ShutdownAlreadyRunning();
                break;
            default:
                break;
        }
    }

    /// <summary>One server tick, called from the physics process of the main thread.</summary>
    public void Tick()
    {
        switch (RuntimeState)
        {
            case ServerRuntimeState.Running or ServerRuntimeState.SafeMode:
                TickRunning();
                break;
            case ServerRuntimeState.ShuttingDown or ServerRuntimeState.Faulted:
                TickStopping();
                break;
            default:
                break;
        }
    }

    private static bool ResultOf(Task<bool> completed) => completed.IsCompletedSuccessfully && completed.GetAwaiter().GetResult();

    private DisconnectCode ShutdownCode => _mode == ProcessMode.DedicatedServer ? DisconnectCode.ServerShutdown : DisconnectCode.HostLeft;

    private void TickRunning()
    {
        try
        {
            PumpWriterEvents();
            if (RuntimeState is not (ServerRuntimeState.Running or ServerRuntimeState.SafeMode))
            {
                return;
            }

            _session!.Poll(_worldState!.CurrentTick);
            if (_simulation!.IsFaulted)
            {
                EnterFaulted("the simulation failed while applying an operation", null);
                return;
            }

            foreach (var participant in _participants)
            {
                participant.OnTick(this);
            }

            _simulation.AdvanceTick();
            ScheduleCheckpoint();
        }
#pragma warning disable CA1031 // Any failure of the tick stops the server with the journal intact (spec: no checkpoint after a simulation error); it is logged by EnterFaulted.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            EnterFaulted("the tick failed", ex);
        }
    }

    private void TickStopping()
    {
        try
        {
            PumpWriterEvents();
            if (_shutdown!.Step())
            {
                FinishShutdown();
            }
        }
#pragma warning disable CA1031 // The shutdown must reach its end; the failure is logged, marked and the process exits with code 70.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Faulted("the shutdown failed", ex);
            _shutdown!.MarkError();
        }
    }

    private void ScheduleCheckpoint()
    {
        if (RuntimeState != ServerRuntimeState.Running || _writer is null || _worldState!.IsCheckpointInFlight)
        {
            return;
        }

        var due = _saveRequested || _terrain!.NeedsEarlyCheckpoint || _time.GetElapsedTime(_lastCheckpointAt) >= _options.CheckpointInterval;
        if (!due)
        {
            return;
        }

        _saveRequested = false;
        _lastCheckpointAt = _time.GetTimestamp();

        // The result arrives as a writer event (committed or failed); the task is only the writer's own completion signal.
        _ = StartCheckpoint();
    }

    private Task<bool> StartCheckpoint()
    {
        var core = _worldState!.CaptureCheckpoint();
        try
        {
            return _writer!.CheckpointAsync(_terrain!.CreateCheckpointRequest(core));
        }
        catch
        {
            // A request that cannot be built or queued is a bug in our code: give the captured changes back and let the caller stop.
            _worldState.RequeueAfterFailedCheckpoint(core);
            throw;
        }
    }

    private void PumpWriterEvents()
    {
        if (_writer is null)
        {
            return;
        }

        while (_writer.TryDequeueEvent(out var evt))
        {
            switch (evt)
            {
                case CheckpointCommitted committed:
                    _worldState!.CompleteCheckpoint(committed.Request.Core);
                    _terrain!.OnCheckpointCommitted(committed.Request);
                    _logger.CheckpointCommitted(committed.Request.Core.BarrierOpId.Value);
                    break;
                case CheckpointFailed failed:
                    _worldState!.RequeueAfterFailedCheckpoint(failed.Request.Core);
                    _terrain!.OnCheckpointFailed(failed.Request);
                    _logger.CheckpointFailed(failed.Request.Core.BarrierOpId.Value);
                    break;
                case WriterFaulted faulted:
                    _logger.WriterFaulted(faulted.Error);
                    if (RuntimeState == ServerRuntimeState.Running)
                    {
                        RuntimeState = ServerRuntimeState.SafeMode;
                    }

                    break;
                case JournalCommitted:
                    LeaveSafeModeIfRecovered();
                    break;
                default:
                    break;
            }
        }

        if (_clearRequested)
        {
            LeaveSafeModeIfRecovered();
        }
    }

    private void LeaveSafeModeIfRecovered()
    {
        if (RuntimeState == ServerRuntimeState.SafeMode && !_readOnlyStart && _writer is { IsFaulted: false })
        {
            RuntimeState = ServerRuntimeState.Running;
            _clearRequested = false;
            _logger.WriterRecovered();
        }
    }

    private void EnterFaulted(string reason, Exception? exception)
    {
        _logger.Faulted(reason, exception);
        ExitCode = 70;
        RuntimeState = ServerRuntimeState.Faulted;
        _shutdown = BuildShutdown(faulted: true);
    }

    private void BeginShutdown(bool faulted)
    {
        _logger.ShutdownRequested(ShutdownCode);
        RuntimeState = ServerRuntimeState.ShuttingDown;
        _shutdown = BuildShutdown(faulted);
    }

    private void FinishShutdown()
    {
        ExitCode = _shutdown!.HadError ? 70 : 0;
        if (RuntimeState == ServerRuntimeState.ShuttingDown)
        {
            RuntimeState = ServerRuntimeState.Stopped;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.Stopped(StateHasher.Hash(_worldState!).ToString("X16", CultureInfo.InvariantCulture));
        }
    }
}
