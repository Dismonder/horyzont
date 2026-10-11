using Horyzont.Core.Protocol;

namespace Horyzont.Client.UI;

/// <summary>Main-thread menu state machine; session shutdown must complete before another start.</summary>
public sealed class MenuController : IGameplayInputGate
{
    private const byte WriterFaultReason = 2;

    private MenuState _returnState = MenuState.Main;
    private UiRelaunchKind _relaunch;

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

    /// <summary>Gets the reason of the latest server status (0 running, 1 terrain mismatch, 2 writer fault).</summary>
    public byte StatusReason { get; private set; }

    /// <summary>Gets the question waiting for an answer.</summary>
    public UiConfirmation Confirmation { get; private set; }

    /// <summary>Gets what is started once the previous session is released.</summary>
    public UiRelaunchKind PendingRelaunch => _relaunch;

    /// <summary>Gets the lobby of a pending invite.</summary>
    public ulong? PendingLobbyId { get; private set; }

    /// <summary>Gets a value indicating whether the local player owns the world (solo or host), not a joined session.</summary>
    public bool IsOwner => _returnState == MenuState.Worlds;

    /// <inheritdoc />
    public bool IsBlocked => State != MenuState.Playing;

    /// <summary>Gets whether tool intents are blocked by UI or server status.</summary>
    public bool AreToolsBlocked => IsBlocked || ReadOnly;

    /// <summary>Gets whether the persistent writer warning is shown.</summary>
    public bool ShowWriterBanner => State is MenuState.Playing or MenuState.Pause && ReadOnly && StatusReason == WriterFaultReason;

    /// <summary>Gets whether the owner may ask for a save (also the retry after a writer fault).</summary>
    public bool CanSave => IsOwner && State is MenuState.Playing or MenuState.Pause;

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
        StatusReason = 0;
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
            Confirmation = UiConfirmation.None;
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
            _relaunch = UiRelaunchKind.None;
            PendingLobbyId = null;
            Fail("ui.release_failed", "Previous session could not be released. Restart the game.");
            return;
        }

        State = _relaunch == UiRelaunchKind.ConnectLobby ? MenuState.Join : _returnState;
        ReadOnly = false;
        StatusReason = 0;
    }

    /// <summary>Hands out the pending relaunch once the previous session has been released; never while one still runs.</summary>
    /// <returns>The relaunch, or <see cref="UiRelaunchKind.None"/>.</returns>
    public UiRelaunchKind TakeRelaunch()
    {
        if (State == MenuState.Stopping || StartsBlocked)
        {
            return UiRelaunchKind.None;
        }

        var kind = _relaunch;
        _relaunch = UiRelaunchKind.None;
        return kind;
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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(detail);
        ErrorKey = key;
        ErrorDetail = detail;
        Confirmation = UiConfirmation.None;
        State = MenuState.Error;
    }

    /// <summary>Shows the refusal of a handshake, unless a more specific error is already shown.</summary>
    /// <param name="code">The refusal.</param>
    /// <param name="detail">Detail from the server.</param>
    public void Rejected(HandshakeRejectCode code, string detail)
    {
        if (State is MenuState.Loading or MenuState.Playing or MenuState.Pause)
        {
            Fail(UiErrorMapper.KeyFor(code), detail);
        }
    }

    /// <summary>Shows the refusal of a server that cannot accept players now, unless a more specific error is already shown.</summary>
    /// <param name="reason">The reason of the message.</param>
    /// <param name="detail">Technical detail.</param>
    public void JoinRefused(byte reason, string detail)
    {
        if (State is MenuState.Loading or MenuState.Playing or MenuState.Pause)
        {
            Fail(UiErrorMapper.KeyForJoinUnavailable(reason), detail);
        }
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

    /// <summary>Applies the latest availability of the server.</summary>
    /// <param name="readOnly">Whether changes are suspended.</param>
    /// <param name="reason">0 running, 1 terrain mismatch, 2 writer fault.</param>
    public void ApplyServerStatus(bool readOnly, byte reason)
    {
        ReadOnly = readOnly;
        StatusReason = readOnly ? reason : (byte)0;
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

    /// <summary>Asks to replace the world file with the newest backup.</summary>
    /// <returns>True when the question is shown.</returns>
    public bool RequestRestore()
    {
        if (!CanRestore || Confirmation != UiConfirmation.None)
        {
            return false;
        }

        Confirmation = UiConfirmation.Restore;
        return true;
    }

    /// <summary>Asks to drop the changes after the last checkpoint.</summary>
    /// <returns>True when the question is shown.</returns>
    public bool RequestRecover()
    {
        if (!CanRecover || Confirmation != UiConfirmation.None)
        {
            return false;
        }

        Confirmation = UiConfirmation.Recover;
        return true;
    }

    /// <summary>Offers a lobby invite. In a menu screen it is taken at once; during a session the player is asked first.</summary>
    /// <param name="lobbyId">The lobby.</param>
    /// <returns>False when starts are blocked or another question is open.</returns>
    public bool OfferInvite(ulong lobbyId)
    {
        if (StartsBlocked || State == MenuState.Stopping || Confirmation is UiConfirmation.Restore or UiConfirmation.Recover)
        {
            return false;
        }

        PendingLobbyId = lobbyId;
        if (State is MenuState.Main or MenuState.Worlds or MenuState.Join)
        {
            _relaunch = UiRelaunchKind.ConnectLobby;
            State = MenuState.Stopping;
            return true;
        }

        Confirmation = UiConfirmation.Invite;
        return true;
    }

    /// <summary>Answers the open question; consent first stops the running session, the action follows through <see cref="TakeRelaunch"/>.</summary>
    /// <param name="accepted">Whether the player agreed.</param>
    public void Confirm(bool accepted)
    {
        var question = Confirmation;
        Confirmation = UiConfirmation.None;
        if (question == UiConfirmation.None)
        {
            return;
        }

        if (!accepted)
        {
            if (question == UiConfirmation.Invite)
            {
                PendingLobbyId = null;
            }

            return;
        }

        switch (question)
        {
            case UiConfirmation.Restore when CanRestore:
                Relaunch(UiRelaunchKind.RestoreLatest);
                break;
            case UiConfirmation.Recover when CanRecover:
                Relaunch(UiRelaunchKind.RecoverFromCheckpoint);
                break;
            case UiConfirmation.Invite when PendingLobbyId is not null:
                Relaunch(UiRelaunchKind.ConnectLobby);
                break;
            default:
                break;
        }
    }

    private void Relaunch(UiRelaunchKind kind)
    {
        _relaunch = kind;
        State = MenuState.Stopping;
    }

    private bool CanNavigate() => !StartsBlocked && State is MenuState.Main or MenuState.Worlds or MenuState.Join;
}
