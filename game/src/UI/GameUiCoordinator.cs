using Godot;

using Horyzont.Client.Diagnostics;
using Horyzont.Client.Session;
using Horyzont.Client.Steam;
using Horyzont.Client.UI;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Boot;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Client.Tools;
using Horyzont.Server;

using Microsoft.Extensions.Logging;

using ServerProcessMode = Horyzont.Server.ProcessMode;

namespace Horyzont.Game.UI;

/// <summary>
/// The menu of the process (K3-18): owns the <see cref="MenuController"/>, the process-level <see cref="QuitCoordinator"/> and at most
/// one <see cref="GameComposition"/> at a time. It builds sessions only through <see cref="GameComposition.Create"/> (it is not a
/// second composition root), never starts a world before the player asks, and never starts the next one before the previous session
/// has been released. Main thread only.
/// </summary>
public sealed partial class GameUiCoordinator : Node, IGameplayInputGate
{
    /// <summary>Longest wait for the release of a finished session, the same as the release deadline of <see cref="GameComposition"/>.</summary>
    internal static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(120);
    private static readonly StringName _pauseAction = new("release_mouse");

    private readonly MenuController _menu = new();
    private readonly WorldCatalog _catalog = new();
    private GameEnvironment _env = null!;
    private ILogger<GameUiCoordinator> _logger = null!;
    private IUiLobbyService _lobby = new UnavailableUiLobbyService();
    private Func<bool>? _platformShutdown;
    private QuitCoordinator? _quit;
    private MainMenu _main = null!;
    private WorldListPanel _worlds = null!;
    private JoinPanel _join = null!;
    private LoadingPanel _loading = null!;
    private ErrorDialog _error = null!;
    private PauseMenu _pause = null!;
    private DeveloperHud _hud = null!;
    private Control _banner = null!;
    private Control _retrySave = null!;
    private GameComposition? _current;
    private LaunchOptions? _lastLaunch;
    private ulong _steamLobby;
    private bool _friendsShown;
    private SessionListener? _listener;
    private GameHudMetricsSource? _metrics;
    private Task? _launchTask;
    private CancellationTokenSource? _launchCts;
    private CancellationTokenSource? _listCts;
    private bool _startFailed;
    private bool _quitRequested;
    private bool _stopBegun;
    private long _stopStarted;
    private long _releaseStarted;
    private bool _releaseTimerRunning;
    private bool _wasModal = true;
    private MenuState _shown = (MenuState)(-1);
    private UiConfirmation _shownConfirmation;
    private string? _shownError;

    /// <inheritdoc />
    public bool IsBlocked => _menu.IsBlocked || _menu.Confirmation != UiConfirmation.None;

    /// <summary>Gets the menu state machine (tests).</summary>
    public MenuController Menu => _menu;

    /// <summary>Gets the running session, or null (tests).</summary>
    public GameComposition? Current => _current;

    /// <summary>Gets the process-level quit coordinator (tests).</summary>
    public QuitCoordinator? Quit => _quit;

    /// <summary>Gives the coordinator its dependencies; call before it enters the tree.</summary>
    /// <param name="environment">The process environment shared by every session; the coordinator turns off the quit management of the sessions.</param>
    /// <param name="lobby">The lobby platform; null for none.</param>
    /// <param name="platformShutdown">Called once per frame after the last session was released at quit until it returns true (Steam shutdown); null for none.</param>
    /// <param name="registerSignals">Whether the process-level quit coordinator reacts to process signals.</param>
    public void Initialize(GameEnvironment environment, IUiLobbyService? lobby, Func<bool>? platformShutdown, bool registerSignals = true)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _env = environment with { ManageQuit = false, RegisterSignals = false, ConsoleInput = null };
        _logger = environment.Loggers.CreateLogger<GameUiCoordinator>();
        _lobby = lobby ?? new UnavailableUiLobbyService();
        _platformShutdown = platformShutdown;
        _quit = new QuitCoordinator { Name = "Quit" };
        _quit.Initialize(
            RequestProcessShutdown, () => _quitRequested && (_current is null || SessionFinished()), () => 0,
            () => (_current?.ReleaseStep() ?? true) && (_platformShutdown?.Invoke() ?? true), registerSignals,
            environment.Loggers.CreateLogger<QuitCoordinator>(), environment.Time, ReleaseTimeout);
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_quit is null)
        {
            throw new InvalidOperationException("GameUiCoordinator.Initialize was not called before the node entered the tree.");
        }

        AddChild(_quit);
        var screens = new CanvasLayer { Name = "Screens", Layer = 20 };
        AddChild(screens);
        _main = Add<MainMenu>(screens, "MainMenu");
        _worlds = Add<WorldListPanel>(screens, "WorldListPanel");
        _join = Add<JoinPanel>(screens, "JoinPanel");
        _loading = Add<LoadingPanel>(screens, "LoadingPanel");
        _error = Add<ErrorDialog>(screens, "ErrorDialog");
        _pause = Add<PauseMenu>(screens, "PauseMenu");
        _hud = GD.Load<PackedScene>("res://scenes/UI/DeveloperHud.tscn").Instantiate<DeveloperHud>();
        AddChild(_hud);
        BuildBanner();
        Wire();
        ProcessMode = ProcessModeEnum.Always;
        ShowState(force: true);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _listCts?.Cancel();
        _launchCts?.Cancel();
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(_pauseAction) || @event.IsEcho())
        {
            return;
        }

        if (_menu.State == MenuState.Playing)
        {
            _menu.Pause();
            GetViewport().SetInputAsHandled();
        }
        else if (_menu.State == MenuState.Pause)
        {
            Resume();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        PumpStop();
        PumpRelaunch();
        ShowState(force: false);
    }

    private static T Add<T>(Node parent, string scene)
        where T : Node
    {
        var node = GD.Load<PackedScene>($"res://scenes/UI/{scene}.tscn").Instantiate<T>();
        parent.AddChild(node);
        return node;
    }

    private static UiStartOutcome Failure(UiStartKind kind, string message) => new(kind, message, 0, null, null, null, false);

    private static ulong RandomSeed()
    {
        // Outside the simulation: the seed is chosen once by the player's action and stored in the world.
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private bool SessionFinished() => _startFailed || (_current?.IsFinished ?? true);

    private void BuildBanner()
    {
        var layer = new CanvasLayer { Name = "WriterBanner", Layer = 30 };
        var box = new HBoxContainer { Name = "Banner" };
        box.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        box.OffsetTop = 8f;
        var label = new Label { Text = "ui.writer_banner" };
        label.AddThemeColorOverride("font_color", Colors.Orange);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
        var retry = new Button { Name = "RetrySaveButton", Text = "ui.writer_retry" };
        retry.Pressed += RequestSave;
        box.AddChild(label);
        box.AddChild(retry);
        layer.AddChild(box);
        AddChild(layer);
        box.Visible = false;
        _banner = box;
        _retrySave = retry;
    }

    private void Wire()
    {
        _main.SoloPressed += () => OpenWorlds(UiLaunchMode.Solo);
        _main.HostPressed += () => OpenWorlds(UiLaunchMode.Host);
        _main.JoinPressed += () =>
        {
            _menu.OpenJoin();
            _join.Prepare(_lobby.Available);
        };
        _main.QuitPressed += () => _quit!.RequestStop();

        _worlds.BackPressed += _menu.Cancel;
        _worlds.StartRequested += id => LaunchLocal(id, seed: null);
        _worlds.CreateRequested += seed => LaunchLocal(Guid.NewGuid(), seed ?? RandomSeed());

        _join.BackPressed += _menu.Cancel;
        _join.ConnectRequested += (host, port, password) => LaunchRemote(host, port, password);
        _join.SteamJoinRequested += id => _lobby.Join(id);
        _join.SteamPasswordEntered += LaunchSteamClient;

        _loading.CancelPressed += _menu.Cancel;

        _error.BackPressed += () =>
        {
            if (_menu.StartsBlocked)
            {
                _quit!.RequestStop();
            }
            else
            {
                _menu.Cancel();
            }
        };
        _error.RestoreRequested += () => _menu.RequestRestore();
        _error.RecoverRequested += () => _menu.RequestRecover();
        _error.Answered += accepted =>
        {
            var wasInvite = _menu.Confirmation == UiConfirmation.Invite;
            _menu.Confirm(accepted);
            if (wasInvite && !accepted)
            {
                _lobby.DeclineJoin();
            }
        };

        _pause.ResumePressed += Resume;
        _pause.SavePressed += RequestSave;
        _pause.InvitePressed += _lobby.Invite;
        _pause.ReturnPressed += _menu.Cancel;
        _pause.QuitPressed += () => _quit!.RequestStop();

        _lobby.JoinConfirmationRequested += id => _menu.OfferInvite(id);
        _lobby.Failed += ShowLobbyError;
        _lobby.ConnectRequested += OnLobbyConnectRequested;
        _lobby.FriendsChanged += RefreshFriends;
    }

    private void OpenWorlds(UiLaunchMode mode)
    {
        _menu.OpenWorlds(mode);
        _worlds.Prepare(mode == UiLaunchMode.Host, _lobby.Available);
        _ = RefreshWorldsAsync();
    }

    private async Task RefreshWorldsAsync()
    {
        _listCts?.Cancel();
        var cts = new CancellationTokenSource();
        _listCts = cts;
        try
        {
            var worlds = await _catalog.ListAsync(Path.Combine(_env.UserDataDir, "saves"), cts.Token).ConfigureAwait(true);
            if (!cts.IsCancellationRequested)
            {
                _worlds.SetWorlds(worlds, _catalog.LimitExceeded);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer scan or leaving the screen replaced this one.
        }
#pragma warning disable CA1031 // A scan failure is shown as an empty list with the log entry; it must not close the menu.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UiLog.WorldScanFailed(_logger, ex);
            _worlds.SetWorlds([], truncated: false);
        }
    }

    private void RefreshFriends()
    {
        var friends = new UiLobbyFriend[64];
        var count = _lobby.CopyFriends(friends);
        _join.SetFriends(friends.AsSpan(0, count));
    }

    private void LaunchLocal(Guid world, ulong? seed)
    {
        var mode = _menu.Mode == UiLaunchMode.Host ? ServerProcessMode.Host : ServerProcessMode.Solo;
        var launch = new LaunchOptions { Mode = mode, WorldId = world, GalaxySeed = seed ?? DevModeArguments.DefaultGalaxySeed };
        if (mode == ServerProcessMode.Host && _worlds.UseSteam && _lobby.Available && _env.Platform is not null)
        {
            // The session binder of K3-17 adds the P2P transport and publishes the lobby once the world runs.
            launch = launch with { EnableSteam = true, LobbyVisibility = SteamUiLobbyService.MapVisibility(_worlds.Visibility) };
        }

        BeginLaunch(launch);
    }

    private void LaunchRemote(string host, int port, string password) =>
        BeginLaunch(new LaunchOptions { Mode = ServerProcessMode.Client, ConnectHost = host, ConnectPort = port, Password = password });

    internal void BeginLaunch(LaunchOptions launch)
    {
        if (!_menu.TryBeginLaunch())
        {
            return;
        }

        _lastLaunch = launch;
        _worlds.SetBusy(true);
        _join.SetBusy(true);
        _launchCts = new CancellationTokenSource();
        _launchTask = RunLaunchAsync(launch, _launchCts.Token);
    }

    private async Task RunLaunchAsync(LaunchOptions launch, CancellationToken ct)
    {
        GameComposition composition;
        try
        {
            composition = GameComposition.Create(launch, _env);
        }
#pragma warning disable CA1031 // The menu stays usable: any failure to build the session is shown with its message and logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UiLog.SessionNotCreated(_logger, ex);
            _startFailed = true;
            _menu.CompleteStart(Failure(UiStartKind.StartFailed, ex.Message));
            return;
        }

        _current = composition;
        _startFailed = false;
        if (_env.Platform is { } platform)
        {
            // Any session, Steam or not, makes an invite ask first instead of leaving the world under the player.
            platform.SessionActive = true;
        }

        Attach(composition);
        AddChild(composition.Root);
        try
        {
            var exit = await composition.StartAsync(ct, connectClient: launch.Mode == ServerProcessMode.Client).ConfigureAwait(true);
            if (exit is not null)
            {
                _startFailed = true;
                _menu.CompleteStart(composition.LastWorldStartResult is { } result
                    ? WorldStartOutcomeMapper.Map(result)
                    : ct.IsCancellationRequested
                        ? Failure(UiStartKind.Aborted, string.Empty)
                        : Failure(UiStartKind.StartFailed, Tr("ui.restore_failed")));
                return;
            }

            if (_menu.State != MenuState.Loading)
            {
                // Cancelled while the world was starting: the stop in progress shuts the session down.
                return;
            }

            if (launch.Mode == ServerProcessMode.Client)
            {
                // The remote session reports its result through the listener: the hello, a refusal or a disconnect.
                return;
            }

            if (composition.LastWorldStartResult is WorldStartResult.StartedReadOnly readOnly)
            {
                _menu.CompleteStart(WorldStartOutcomeMapper.Map(readOnly));
                return;
            }

            composition.ConnectLocalClient();
            _menu.CompleteStart(Failure(UiStartKind.Started, string.Empty));
        }
        catch (OperationCanceledException)
        {
            _startFailed = true;
            _menu.CompleteStart(Failure(UiStartKind.Aborted, string.Empty));
        }
#pragma warning disable CA1031 // The start failed in an unexpected way: show it, log it and release the half built session.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UiLog.SessionStartFailed(_logger, ex);
            _startFailed = true;
            composition.Release();
            _menu.CompleteStart(Failure(UiStartKind.StartFailed, ex.Message));
        }
    }

    private void Attach(GameComposition composition)
    {
        if (composition.ClientNode?.GetNodeOrNull<LocalInputReader>("Input") is { } reader)
        {
            reader.Gate = this;
        }

        if (composition.Tools?.GetNodeOrNull<ToolInputReader>("Input") is { } toolReader)
        {
            toolReader.Gate = this;
        }

        _listener = new SessionListener(this);
        composition.Client?.AddListener(_listener);
        _metrics = new GameHudMetricsSource(composition, () => _menu.ReadOnly);
        _hud.Bind(new HudSampler(_metrics, _env.Time), () => composition.Tools);
    }

    private void Detach()
    {
        if (_current is { Client: { } client } && _listener is not null)
        {
            client.RemoveListener(_listener);
        }

        _listener = null;
        _metrics?.Detach();
        _metrics = null;
        _hud.Bind(null, null);
        _lobby.Leave();
    }

    private void PumpStop()
    {
        if (_menu.State != MenuState.Stopping)
        {
            _stopBegun = false;
            _releaseTimerRunning = false;
            return;
        }

        if (!_stopBegun)
        {
            _stopBegun = true;
            _stopStarted = _env.Time.GetTimestamp();
            _launchCts?.Cancel();
            if (_current is not null && !_startFailed)
            {
                _current.RequestShutdown();
            }
        }

        if (_launchTask is { IsCompleted: false })
        {
            // The start is giving up (the cancellation reaches the server); the shutdown continues when it returned.
            return;
        }

        if (!SessionFinished())
        {
            if (_env.Time.GetElapsedTime(_stopStarted) > _shutdownTimeout)
            {
                FailRelease();
            }

            return;
        }

        if (!_releaseTimerRunning)
        {
            _releaseTimerRunning = true;
            _releaseStarted = _env.Time.GetTimestamp();
        }

        if (_current?.ReleaseStep() ?? true)
        {
            DisposeCurrent();
            _menu.CompleteStop(released: true);
        }
        else if (_env.Time.GetElapsedTime(_releaseStarted) > ReleaseTimeout)
        {
            FailRelease();
        }
    }

    private void FailRelease()
    {
        UiLog.ReleaseFailed(_logger);
        _menu.CompleteStop(released: false);
    }

    private void DisposeCurrent()
    {
        if (_current is null)
        {
            return;
        }

        Detach();
        var root = _current.Root;
        _current = null;
        if (_env.Platform is { } platform)
        {
            platform.SessionActive = false;
        }

        _startFailed = false;
        _launchTask = null;
        if (root.GetParent() is { } parent)
        {
            parent.RemoveChild(root);
        }

        root.QueueFree();
    }

    private void PumpRelaunch()
    {
        if (_quitRequested || _menu.State == MenuState.Stopping)
        {
            return;
        }

        var kind = _menu.TakeRelaunch();
        switch (kind)
        {
            case UiRelaunchKind.RestoreLatest when _lastLaunch is { } last:
                BeginLaunch(last with { RestoreLatestBackup = true, RecoverFromCheckpoint = false });
                break;
            case UiRelaunchKind.RecoverFromCheckpoint when _lastLaunch is { } last:
                BeginLaunch(last with { RecoverFromCheckpoint = true, RestoreLatestBackup = false });
                break;
            case UiRelaunchKind.ConnectLobby when _menu.PendingLobbyId is { } lobbyId:
                _lobby.ConfirmJoin(lobbyId);
                _join.Prepare(_lobby.Available);
                break;
            default:
                break;
        }
    }

    private void OnLobbyConnectRequested(ulong lobbyId, bool passwordRequired)
    {
        // A lobby passed every check outside of a session (a friend picked in the menu, or an accepted invite).
        if (_menu.State is MenuState.Main or MenuState.Worlds)
        {
            _menu.OpenJoin();
        }

        if (_menu.State != MenuState.Join)
        {
            UiLog.LobbyConnectIgnored(_logger, lobbyId, _menu.State);
            return;
        }

        _steamLobby = lobbyId;
        if (passwordRequired)
        {
            _join.PrepareSteamPassword();
            return;
        }

        LaunchSteamClient(string.Empty);
    }

    private void LaunchSteamClient(string password)
    {
        var lobbyId = _steamLobby;
        if (lobbyId == 0 || _env.Platform is null)
        {
            ShowLobbyError(UiLobbyError.Unavailable);
            return;
        }

        _steamLobby = 0;

        // The session joins the lobby again through its own binder, which needs the controller idle.
        _lobby.Leave();
        BeginLaunch(new LaunchOptions
        {
            Mode = ServerProcessMode.Client,
            EnableSteam = true,
            ConnectLobbyId = lobbyId,
            Password = password,
        });
    }

    private void ShowLobbyError(UiLobbyError error)
    {
        // A Steam client has nothing to wait for once its lobby failed (its session ends itself); a host keeps its world.
        var clientLost = _menu.State is MenuState.Loading or MenuState.Playing or MenuState.Pause
            && _lastLaunch is { Mode: ServerProcessMode.Client, EnableSteam: true };
        if (clientLost || _menu.State is MenuState.Main or MenuState.Worlds or MenuState.Join)
        {
            _menu.Fail("ui.lobby_failed", error.ToString());
        }
        else
        {
            UiLog.LobbyFailedInSession(_logger, error);
        }
    }

    private void RequestSave()
    {
        if (_menu.CanSave && _current?.Server is { RuntimeState: ServerRuntimeState.Running or ServerRuntimeState.SafeMode } server)
        {
            server.RequestCheckpoint();
        }
    }

    private void Resume()
    {
        _menu.Resume();
    }

    private void RequestProcessShutdown()
    {
        _quitRequested = true;
        if (_menu.State is MenuState.Loading or MenuState.Playing or MenuState.Pause or MenuState.Error)
        {
            _menu.Cancel();
        }

        if (_current is not null && !_startFailed)
        {
            _current.RequestShutdown();
        }
    }

    private void ShowState(bool force)
    {
        var state = _menu.State;
        var error = _menu.ErrorKey;
        if (!force && state == _shown && _menu.Confirmation == _shownConfirmation && error == _shownError)
        {
            return;
        }

        _shown = state;
        _shownConfirmation = _menu.Confirmation;
        _shownError = error;

        _main.Visible = state == MenuState.Main;
        _worlds.Visible = state == MenuState.Worlds;
        _join.Visible = state == MenuState.Join;
        _loading.Visible = state is MenuState.Loading or MenuState.Stopping;
        _error.Visible = state == MenuState.Error || (_menu.Confirmation == UiConfirmation.Invite && state is MenuState.Playing or MenuState.Pause);
        _pause.Visible = state == MenuState.Pause && _menu.Confirmation == UiConfirmation.None;
        _main.SetStartsEnabled(!_menu.StartsBlocked);
        SetFriendsShown(state == MenuState.Join);
        if (state is MenuState.Worlds or MenuState.Join)
        {
            _worlds.SetBusy(false);
            _join.SetBusy(false);
        }

        if (state == MenuState.Loading)
        {
            _loading.SetStage(_lastLaunch?.Mode == ServerProcessMode.Client ? "ui.loading_connect" : "ui.loading_world", cancellable: true);
        }
        else if (state == MenuState.Stopping)
        {
            _loading.SetStage("ui.stopping", cancellable: false);
        }
        else if (state == MenuState.Error)
        {
            _error.ShowError(_menu);
        }
        else if (state == MenuState.Pause)
        {
            _pause.Prepare(_menu.CanSave, _lobby.State is UiLobbyState.Hosting or UiLobbyState.Joined);
        }

        if (_error.Visible && state is MenuState.Playing or MenuState.Pause)
        {
            _error.ShowQuestion(_menu);
        }

        var modal = IsBlocked;
        if (modal)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (_wasModal)
        {
            RecaptureMouse();
        }

        _wasModal = modal;

        _banner.Visible = _menu.ShowWriterBanner;
        _retrySave.Visible = _menu.CanSave;
    }

    private void SetFriendsShown(bool shown)
    {
        if (shown == _friendsShown)
        {
            return;
        }

        _friendsShown = shown;
        _lobby.SetFriendsVisible(shown);
    }

    private void RecaptureMouse()
    {
        if (_current?.ClientNode?.GetNodeOrNull<LocalInputReader>("Input") is { } reader)
        {
            reader.Recapture();
        }
    }

    private sealed class SessionListener(GameUiCoordinator owner) : IClientSessionListener
    {
        public void OnServerHello(ServerHello hello)
        {
            if (owner._lastLaunch?.Mode == ServerProcessMode.Client)
            {
                owner._menu.CompleteStart(Failure(UiStartKind.Started, string.Empty));
            }
        }

        public void OnRejected(HandshakeRejectCode code, string detail) => owner._menu.Rejected(code, detail);

        public void OnMessage(Message message)
        {
            switch (message)
            {
                case ServerStatus status:
                    owner._menu.ApplyServerStatus(status.ReadOnly, status.Reason);
                    break;
                case JoinUnavailable unavailable:
                    owner._menu.JoinRefused(unavailable.Reason, unavailable.Detail);
                    break;
                default:
                    break;
            }
        }

        public void OnDisconnected(DisconnectCode code) => owner._menu.SessionEnded(code);
    }
}

internal static partial class UiLog
{
    [LoggerMessage(EventId = 9301, Level = LogLevel.Warning, Message = "scanning the world list failed")]
    internal static partial void WorldScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9302, Level = LogLevel.Error, Message = "the session could not be created")]
    internal static partial void SessionNotCreated(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9303, Level = LogLevel.Error, Message = "the session failed to start")]
    internal static partial void SessionStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9304, Level = LogLevel.Error, Message = "the previous session could not be released; new starts are blocked")]
    internal static partial void ReleaseFailed(ILogger logger);

    [LoggerMessage(EventId = 9306, Level = LogLevel.Warning, Message = "lobby failure {Error} during a session; the game goes on")]
    internal static partial void LobbyFailedInSession(ILogger logger, UiLobbyError error);

    [LoggerMessage(EventId = 9305, Level = LogLevel.Warning, Message = "lobby {Lobby} was joined while the menu was in state {State}; the connection is dropped")]
    internal static partial void LobbyConnectIgnored(ILogger logger, ulong lobby, MenuState state);
}
