using Horyzont.Client.Diagnostics;
using Horyzont.Client.UI;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;

namespace Horyzont.Client.Tests.UI;

public sealed class MenuFlowTests
{
    [Fact]
    public void Restore_CorruptedWithBackups_RequiresConfirmationAndRelease()
    {
        var menu = CorruptedMenu(backups: 2);
        Assert.Equal(UiConfirmation.None, menu.Confirmation);

        // Refusal keeps the error and starts nothing.
        Assert.True(menu.RequestRestore());
        Assert.Equal(UiConfirmation.Restore, menu.Confirmation);
        menu.Confirm(accepted: false);
        Assert.Equal(UiConfirmation.None, menu.Confirmation);
        Assert.Equal(MenuState.Error, menu.State);
        Assert.Equal(UiRelaunchKind.None, menu.PendingRelaunch);

        // Consent stops the failed start first; the relaunch is only handed out after the release succeeded.
        Assert.True(menu.RequestRestore());
        menu.Confirm(accepted: true);
        Assert.Equal(MenuState.Stopping, menu.State);
        Assert.Equal(UiRelaunchKind.None, menu.TakeRelaunch());
        menu.CompleteStop(released: true);
        Assert.Equal(MenuState.Worlds, menu.State);
        Assert.Equal(UiRelaunchKind.RestoreLatest, menu.TakeRelaunch());
        Assert.Equal(UiRelaunchKind.None, menu.TakeRelaunch());
        Assert.True(menu.TryBeginLaunch());
    }

    [Fact]
    public void Restore_NoBackupsOrWrongOutcome_OffersNothing()
    {
        var none = CorruptedMenu(backups: 0);
        Assert.False(none.RequestRestore());
        Assert.Equal(UiConfirmation.None, none.Confirmation);

        var menu = new MenuController();
        Assert.False(menu.RequestRestore());
        Assert.False(menu.RequestRecover());
    }

    [Fact]
    public void Restore_IoFailureDuringRelaunch_ShowsErrorAndKeepsStartsPossible()
    {
        var menu = CorruptedMenu(backups: 1);
        menu.RequestRestore();
        menu.Confirm(accepted: true);
        menu.CompleteStop(released: true);
        Assert.Equal(UiRelaunchKind.RestoreLatest, menu.TakeRelaunch());
        Assert.True(menu.TryBeginLaunch());
        menu.CompleteStart(new(UiStartKind.StartFailed, "no backup could be restored", 0, null, null, null, false));
        Assert.Equal(MenuState.Error, menu.State);
        Assert.False(menu.StartsBlocked);
        Assert.False(menu.CanRestore);
    }

    [Fact]
    public void Restore_ReleaseFailure_DropsRelaunchAndBlocksStarts()
    {
        var menu = CorruptedMenu(backups: 1);
        menu.RequestRestore();
        menu.Confirm(accepted: true);
        menu.CompleteStop(released: false);
        Assert.True(menu.StartsBlocked);
        Assert.Equal(UiRelaunchKind.None, menu.TakeRelaunch());
        Assert.Equal(MenuState.Error, menu.State);
    }

    [Fact]
    public void Recover_TerrainSemanticsMismatch_RequiresSeparateConsentAndRelease()
    {
        var menu = new MenuController();
        menu.OpenWorlds(UiLaunchMode.Solo);
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.StartedReadOnly, "mismatch", 0, 42, null, null, true));
        Assert.Equal(MenuState.Error, menu.State);
        Assert.True(menu.IsBlocked);
        Assert.False(menu.CanRestore);

        menu.RequestRecover();
        Assert.Equal(UiConfirmation.Recover, menu.Confirmation);
        menu.Confirm(accepted: false);
        Assert.Equal(UiRelaunchKind.None, menu.PendingRelaunch);
        Assert.Equal(MenuState.Error, menu.State);

        menu.RequestRecover();
        menu.Confirm(accepted: true);
        Assert.Equal(MenuState.Stopping, menu.State);
        menu.CompleteStop(released: true);
        Assert.Equal(UiRelaunchKind.RecoverFromCheckpoint, menu.TakeRelaunch());
    }

    [Fact]
    public void Recover_PlainReadOnlyStart_IsNotOffered()
    {
        var menu = new MenuController();
        menu.OpenWorlds(UiLaunchMode.Host);
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.StartedReadOnly, "disk full", 0, null, null, null, false));
        Assert.False(menu.CanRecover);
        Assert.False(menu.RequestRecover());
    }

    [Fact]
    public void ServerStatus_WriterFault_ShowsBannerUntilRunningIsConfirmed()
    {
        var menu = PlayingJoin();
        Assert.False(menu.ShowWriterBanner);
        menu.ApplyServerStatus(readOnly: true, reason: 2);
        Assert.True(menu.ShowWriterBanner);
        Assert.True(menu.AreToolsBlocked);
        Assert.False(menu.IsBlocked);
        Assert.False(menu.CanSave);

        menu.ApplyServerStatus(readOnly: false, reason: 0);
        Assert.False(menu.ShowWriterBanner);
        Assert.False(menu.AreToolsBlocked);
    }

    [Fact]
    public void ServerStatus_Owner_CanSaveWhilePlayingOrPaused()
    {
        var menu = new MenuController();
        menu.OpenWorlds(UiLaunchMode.Host);
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.Started, "", 0, null, null, null, false));
        Assert.True(menu.CanSave);
        menu.Pause();
        Assert.True(menu.CanSave);
        menu.ApplyServerStatus(readOnly: true, reason: 2);
        Assert.True(menu.ShowWriterBanner);
        Assert.True(menu.CanSave);
    }

    [Theory]
    [InlineData(1, "ui.join_unavailable_terrain")]
    [InlineData(2, "ui.join_unavailable_writer")]
    [InlineData(77, "ui.join_unavailable")]
    public void JoinRefused_KnownAndUnknownReasons_MapToReadableKeys(byte reason, string key)
    {
        var menu = new MenuController();
        menu.OpenJoin();
        menu.BeginLaunch();
        menu.JoinRefused(reason, "detail");
        Assert.Equal(key, menu.ErrorKey);
        Assert.Equal("detail", menu.ErrorDetail);
        Assert.Equal(MenuState.Error, menu.State);

        // The disconnect that follows must not hide the cause.
        menu.SessionEnded(DisconnectCode.Normal);
        Assert.Equal(key, menu.ErrorKey);
    }

    [Theory]
    [InlineData(HandshakeRejectCode.BadPassword, "ui.reject_bad_password")]
    [InlineData(HandshakeRejectCode.WrongBuild, "ui.reject_wrong_build")]
    [InlineData(HandshakeRejectCode.WrongProtocol, "ui.reject_wrong_protocol")]
    [InlineData(HandshakeRejectCode.ServerFull, "ui.reject_full")]
    [InlineData(HandshakeRejectCode.Banned, "ui.reject_banned")]
    public void HandshakeReject_Codes_MapToDistinctKeys(HandshakeRejectCode code, string key) =>
        Assert.Equal(key, UiErrorMapper.KeyFor(code));

    [Theory]
    [InlineData(DisconnectCode.Kicked, "ui.kicked")]
    [InlineData(DisconnectCode.HostLeft, "ui.host_left")]
    [InlineData(DisconnectCode.Timeout, "ui.timeout")]
    public void Disconnect_SpecificCodes_KeepTheirKeys(DisconnectCode code, string key)
    {
        var menu = PlayingJoin();
        menu.SessionEnded(code);
        Assert.Equal(key, menu.ErrorKey);
        Assert.Equal(MenuState.Error, menu.State);
    }

    [Fact]
    public void Steam_InviteDuringGame_WaitsForConfirmationAndOldWorldStop()
    {
        var lobby = new FakeLobby();
        var menu = new MenuController();
        lobby.JoinConfirmationRequested += id => menu.OfferInvite(id);
        menu.OpenWorlds(UiLaunchMode.Host);
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.Started, "", 0, null, null, null, false));

        lobby.RaiseInvite(777);
        Assert.Equal(UiConfirmation.Invite, menu.Confirmation);
        Assert.Equal(MenuState.Playing, menu.State);

        // Refusal changes nothing in the running world.
        menu.Confirm(accepted: false);
        Assert.Equal(MenuState.Playing, menu.State);
        Assert.Null(menu.PendingLobbyId);
        Assert.Equal(UiRelaunchKind.None, menu.TakeRelaunch());

        // Consent first stops the old world; the join is handed out only after a successful release.
        lobby.RaiseInvite(778);
        menu.Confirm(accepted: true);
        Assert.Equal(MenuState.Stopping, menu.State);
        Assert.Equal(UiRelaunchKind.None, menu.TakeRelaunch());
        menu.CompleteStop(released: true);
        Assert.Equal(778UL, menu.PendingLobbyId);
        Assert.Equal(UiRelaunchKind.ConnectLobby, menu.TakeRelaunch());
    }

    [Fact]
    public void Steam_InviteOnMenuScreen_NeedsNoConfirmation()
    {
        var menu = new MenuController();
        Assert.True(menu.OfferInvite(5));
        Assert.Equal(UiConfirmation.None, menu.Confirmation);
        Assert.Equal(MenuState.Stopping, menu.State);
        menu.CompleteStop(released: true);
        Assert.Equal(UiRelaunchKind.ConnectLobby, menu.TakeRelaunch());
        Assert.Equal(5UL, menu.PendingLobbyId);
    }

    [Fact]
    public void Steam_UnavailableService_DoesNothing()
    {
        var service = new UnavailableUiLobbyService();
        Assert.False(service.Available);
        Assert.Equal(UiLobbyState.Unavailable, service.State);
        var friends = new UiLobbyFriend[4];
        Assert.Equal(0, service.CopyFriends(friends));
        UiLobbyError? failure = null;
        service.Failed += error => failure = error;
        service.Host(UiLobbyVisibility.FriendsOnly, 4);
        Assert.Equal(UiLobbyError.Unavailable, failure);
    }

    [Fact]
    public void Steam_BlockedStarts_IgnoreInvites()
    {
        var menu = new MenuController();
        menu.OpenWorlds(UiLaunchMode.Solo);
        menu.BeginLaunch();
        menu.Cancel();
        menu.CompleteStop(released: false);
        Assert.False(menu.OfferInvite(9));
        Assert.Equal(UiRelaunchKind.None, menu.TakeRelaunch());
    }

    [Theory]
    [InlineData(RejectReason.RateLimited, true)]
    [InlineData(RejectReason.OutOfReach, true)]
    [InlineData(RejectReason.ServerReadOnly, false)]
    [InlineData(RejectReason.CellOccupied, false)]
    [InlineData(RejectReason.None, false)]
    public void Hud_ToolsRejectAndConfiguredLimits_ShowsReasonWithoutGuessing(RejectReason reason, bool limitNotice) =>
        Assert.Equal(limitNotice, HudRejectNotice.IsServerLimit(reason));

    private static MenuController CorruptedMenu(int backups)
    {
        var menu = new MenuController();
        menu.OpenWorlds(UiLaunchMode.Solo);
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.Corrupted, "bad db", backups, null, null, null, false));
        return menu;
    }

    private static MenuController PlayingJoin()
    {
        var menu = new MenuController();
        menu.OpenJoin();
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.Started, "", 0, null, null, null, false));
        return menu;
    }

    private sealed class FakeLobby : IUiLobbyService
    {
        public bool Available => true;

        public UiLobbyState State => UiLobbyState.Idle;

        public event Action<ulong>? JoinConfirmationRequested;

        public event Action<UiLobbyError>? Failed;

        public event Action<UiLobbyState>? StateChanged;

        public event Action<ulong, bool>? ConnectRequested;

        public void RaiseInvite(ulong id) => JoinConfirmationRequested?.Invoke(id);

        public void Host(UiLobbyVisibility visibility, int capacity) => StateChanged?.Invoke(UiLobbyState.Hosting);

        public void Join(ulong lobbyId) => ConnectRequested?.Invoke(lobbyId, true);

        public void Invite()
        {
        }

        public void Leave() => Failed?.Invoke(UiLobbyError.Cancelled);

        public int CopyFriends(Span<UiLobbyFriend> into) => 0;

        public void ConfirmJoin(ulong lobbyId)
        {
        }

        public void DeclineJoin()
        {
        }
    }
}
