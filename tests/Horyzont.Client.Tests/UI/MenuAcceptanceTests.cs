using Horyzont.Client.UI;
using Horyzont.Core.Protocol;

namespace Horyzont.Client.Tests.UI;

public sealed class MenuAcceptanceTests
{
    [Fact]
    public void Create_DoubleClick_UsesOneWorldIdAndLaunch()
    {
        var menu = new MenuController();
        Assert.Equal(MenuState.Main, menu.State);
        Assert.True(menu.IsBlocked);
        menu.OpenWorlds(UiLaunchMode.Host);
        Assert.True(menu.TryBeginLaunch());
        Assert.False(menu.TryBeginLaunch());
        Assert.Equal(MenuState.Loading, menu.State);
        menu.Cancel();
        Assert.Equal(MenuState.Stopping, menu.State);
        Assert.False(menu.TryBeginLaunch());
        menu.CompleteStop(true);
        Assert.Equal(MenuState.Worlds, menu.State);
        Assert.True(menu.TryBeginLaunch());
    }

    [Theory]
    [InlineData(UiStartKind.Started, MenuState.Playing)]
    [InlineData(UiStartKind.StartedReadOnly, MenuState.Error)]
    [InlineData(UiStartKind.Corrupted, MenuState.Error)]
    [InlineData(UiStartKind.FromNewerVersion, MenuState.Error)]
    [InlineData(UiStartKind.ReplayFailed, MenuState.Error)]
    [InlineData(UiStartKind.StartFailed, MenuState.Error)]
    [InlineData(UiStartKind.Aborted, MenuState.Stopping)]
    public void Start_AllOutcomes_ShowsExpectedScreen(UiStartKind kind, MenuState expected)
    {
        var menu = new MenuController();
        menu.OpenWorlds(UiLaunchMode.Solo);
        menu.BeginLaunch();
        var outcome = new UiStartOutcome(kind, "detail", 2, 42, 5, 4, true);
        menu.CompleteStart(outcome);
        Assert.Equal(expected, menu.State);
        Assert.Equal(outcome, menu.Outcome);
        Assert.Equal(kind != UiStartKind.Started, menu.IsBlocked);
    }

    [Fact]
    public void ReturnToMenu_ReleaseTimeout_BlocksNewStarts()
    {
        var menu = Playing();
        menu.Cancel();
        menu.CompleteStop(false);
        Assert.Equal(MenuState.Error, menu.State);
        menu.OpenJoin();
        menu.OpenWorlds(UiLaunchMode.Solo);
        Assert.False(menu.TryBeginLaunch());
        Assert.Equal(MenuState.Error, menu.State);
        Assert.True(menu.StartsBlocked);
    }

    [Fact]
    public void Pause_FocusedModal_BlocksGameplayUntilResume()
    {
        var menu = Playing();
        menu.Pause();
        Assert.True(menu.IsBlocked);
        Assert.Equal(MenuState.Pause, menu.State);
        menu.Resume();
        Assert.False(menu.IsBlocked);
        menu.ReadOnly = true;
        Assert.True(menu.AreToolsBlocked);
        Assert.False(menu.IsBlocked);
    }

    [Fact]
    public void Disconnect_AfterSpecificError_PreservesReadableReason()
    {
        var menu = new MenuController();
        menu.OpenJoin();
        menu.BeginLaunch();
        menu.Fail("ui.join_unavailable", "Terrain semantics mismatch");
        menu.SessionEnded(DisconnectCode.Timeout);
        Assert.Equal("ui.join_unavailable", menu.ErrorKey);
        Assert.Equal("Terrain semantics mismatch", menu.ErrorDetail);
        Assert.Equal(MenuState.Error, menu.State);
    }

    [Fact]
    public void Start_LateCompletionAfterCancel_DoesNotEnterPlaying()
    {
        var menu = new MenuController();
        menu.OpenJoin();
        menu.BeginLaunch();
        menu.Cancel();
        menu.CompleteStart(new(UiStartKind.Started, "", 0, null, null, null, false));
        Assert.Equal(MenuState.Stopping, menu.State);
    }

    [Fact]
    public void Restore_RecoveryActions_AreRestrictedToMatchingOutcome()
    {
        var menu = new MenuController();
        menu.OpenWorlds(UiLaunchMode.Solo);
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.Corrupted, "bad db", 1, null, null, null, false));
        Assert.True(menu.CanRestore);
        Assert.False(menu.CanRecover);
        menu.Cancel();
        menu.CompleteStop(true);
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.StartedReadOnly, "mismatch", 0, 42, null, null, true));
        Assert.True(menu.CanRecover);
        Assert.False(menu.CanRestore);
    }

    private static MenuController Playing()
    {
        var menu = new MenuController();
        menu.OpenWorlds(UiLaunchMode.Solo);
        menu.BeginLaunch();
        menu.CompleteStart(new(UiStartKind.Started, "", 0, null, null, null, false));
        return menu;
    }
}
