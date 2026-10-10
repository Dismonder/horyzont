using Horyzont.Core.Protocol;

namespace Horyzont.Client.UI;

/// <summary>Main-thread menu state machine; session shutdown must complete before another start.</summary>
public sealed class MenuController : IGameplayInputGate
{
    private MenuState _returnState = MenuState.Main;

    /// <summary>Gets the current screen.</summary>
    public MenuState State { get; private set; } = MenuState.Main;
    /// <summary>Gets the selected local mode.</summary>
    public UiLaunchMode Mode { get; private set; }
    /// <summary>Gets the latest start result.</summary>
    public UiStartOutcome? Outcome { get; private set; }
    /// <summary>Gets the translation key for an error.</summary>
    public string? ErrorKey { get; private set; }
    /// <summary>Gets the plain text error detail.</summary>
    public string? ErrorDetail { get; private set; }
    /// <summary>Gets whether release failed and future starts are permanently blocked.</summary>
    public bool StartsBlocked { get; private set; }
    /// <summary>Gets or sets the confirmed server read-only status.</summary>
    public bool ReadOnly { get; set; }
    /// <inheritdoc />
    public bool IsBlocked => State != MenuState.Playing;
    /// <summary>Gets whether tool intents are blocked by UI or server status.</summary>
    public bool AreToolsBlocked => IsBlocked || ReadOnly;
    /// <summary>Gets whether a restore confirmation may be offered.</summary>
    public bool CanRestore => !StartsBlocked && State == MenuState.Error
        && Outcome is { Kind: UiStartKind.Corrupted, BackupCount: > 0 };
    /// <summary>Gets whether a checkpoint recovery confirmation may be offered.</summary>
    public bool CanRecover => !StartsBlocked && State == MenuState.Error
        && Outcome is { Kind: UiStartKind.StartedReadOnly, TerrainSemanticsMismatch: true };

    /// <summary>Opens a world list when there is no active launch or session.</summary>
    public void OpenWorlds(UiLaunchMode mode)
    {
        if (!CanNavigate())
        {
            return;
        }
        Mode = mode;
        State = MenuState.Worlds;
    }

    /// <summary>Opens the join form when there is no active launch or session.</summary>
    public void OpenJoin()
    {
        if (!CanNavigate())
        {
            return;
        }
        State = MenuState.Join;
    }

    /// <summary>Begins a launch; repeated requests are ignored.</summary>
    public void BeginLaunch() => TryBeginLaunch();

    /// <summary>Acquires the single launch slot.</summary>
    /// <returns>True only for the first request.</returns>
    public bool TryBeginLaunch()
    {
        if (StartsBlocked || State is not (MenuState.Worlds or MenuState.Join))
        {
            return false;
        }
        _returnState = State;
        Outcome = null;
        ErrorKey = null;
        ErrorDetail = null;
        ReadOnly = false;
        State = MenuState.Loading;
        return true;
    }

    /// <summary>Cancels a launch or session; the caller must drain and release it.</summary>
    public void Cancel()
    {
        if (StartsBlocked)
        {
            return;
        }
        if (State is MenuState.Loading or MenuState.Playing or MenuState.Pause or MenuState.Error)
        {
            State = MenuState.Stopping;
        }
        else if (State is MenuState.Join or MenuState.Worlds)
        {
            State = MenuState.Main;
        }
    }

    /// <summary>Completes shutdown. A failure permanently blocks new sessions.</summary>
    /// <param name="released">Whether all resources have been released.</param>
    public void CompleteStop(bool released)
    {
        if (State != MenuState.Stopping)
        {
            return;
        }
        if (!released)
        {
            StartsBlocked = true;
            Fail("ui.release_failed", "Previous session could not be released. Restart the game.");
            return;
        }
        State = _returnState;
        ReadOnly = false;
    }

    /// <summary>Maps a completed start; a late completion cannot undo cancellation.</summary>
    public void CompleteStart(UiStartOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (State != MenuState.Loading)
        {
            return;
        }
        Outcome = outcome;
        if (outcome.Kind == UiStartKind.Started)
        {
            State = MenuState.Playing;
        }
        else if (outcome.Kind == UiStartKind.Aborted)
        {
            State = MenuState.Stopping;
        }
        else
        {
            Fail(UiErrorMapper.KeyFor(outcome.Kind), outcome.Message);
        }
    }

    /// <summary>Displays a specific error until it is dismissed.</summary>
    public void Fail(string key, string detail)
    {
        ErrorKey = key;
        ErrorDetail = detail;
        State = MenuState.Error;
    }

    /// <summary>Handles disconnect without overwriting an earlier specific failure.</summary>
    public void SessionEnded(DisconnectCode code)
    {
        if (State is MenuState.Error or MenuState.Stopping)
        {
            return;
        }
        Fail(UiErrorMapper.KeyFor(code), "");
    }

    /// <summary>Opens pause while simulation keeps running.</summary>
    public void Pause()
    {
        if (State == MenuState.Playing)
        {
            State = MenuState.Pause;
        }
    }

    /// <summary>Returns focus to gameplay.</summary>
    public void Resume()
    {
        if (State == MenuState.Pause)
        {
            State = MenuState.Playing;
        }
    }

    private bool CanNavigate() => !StartsBlocked && State is MenuState.Main or MenuState.Worlds or MenuState.Join;
}
