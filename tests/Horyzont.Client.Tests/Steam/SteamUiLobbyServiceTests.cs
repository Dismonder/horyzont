using Horyzont.Client.Steam;
using Horyzont.Client.UI;

namespace Horyzont.Client.Tests.Steam;

public sealed class SteamUiLobbyServiceTests
{
    [Fact]
    public void Available_FollowsTheControllerState()
    {
        using var f = new LobbyFixture();
        using var ui = new SteamUiLobbyService(f.Controller);

        Assert.True(ui.Available);
        Assert.Equal(UiLobbyState.Idle, ui.State);

        f.Controller.SetUnavailable();

        Assert.False(ui.Available);
        Assert.Equal(UiLobbyState.Unavailable, ui.State);
    }

    [Fact]
    public void Join_ValidLobby_RaisesConnectWithTheLobbyIdAndPasswordFlag()
    {
        using var f = new LobbyFixture();
        using var ui = new SteamUiLobbyService(f.Controller);
        var connects = new List<(ulong Lobby, bool Password)>();
        var states = new List<UiLobbyState>();
        ui.ConnectRequested += (lobby, password) => connects.Add((lobby, password));
        ui.StateChanged += states.Add;
        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol, passwordRequired: true);

        ui.Join(5000);
        f.Api.RaiseJoined(new LobbyApiResult(f.Api.JoinRequests[^1].RequestId, 5000, LobbyError.None));

        Assert.Equal([(5000UL, true)], connects);
        Assert.Equal([UiLobbyState.Joining, UiLobbyState.Joined], states);
        Assert.Equal(UiLobbyState.Joined, ui.State);
    }

    [Theory]
    [InlineData(LobbyError.Timeout, UiLobbyError.Timeout)]
    [InlineData(LobbyError.Full, UiLobbyError.Full)]
    [InlineData(LobbyError.AccessDenied, UiLobbyError.AccessDenied)]
    [InlineData(LobbyError.PlatformFailure, UiLobbyError.PlatformFailure)]
    [InlineData(LobbyError.Cancelled, UiLobbyError.Cancelled)]
    public void Failed_MapsAJoinErrorOneToOne(LobbyError error, UiLobbyError expected)
    {
        using var f = new LobbyFixture();
        using var ui = new SteamUiLobbyService(f.Controller);
        var seen = new List<UiLobbyError>();
        ui.Failed += seen.Add;
        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);

        ui.Join(5000);
        f.Api.RaiseJoined(new LobbyApiResult(f.Api.JoinRequests[^1].RequestId, 5000, error));

        Assert.Equal([expected], seen);
    }

    [Theory]
    [InlineData(LobbyError.None, UiLobbyError.None)]
    [InlineData(LobbyError.Unavailable, UiLobbyError.Unavailable)]
    [InlineData(LobbyError.Timeout, UiLobbyError.Timeout)]
    [InlineData(LobbyError.Full, UiLobbyError.Full)]
    [InlineData(LobbyError.AccessDenied, UiLobbyError.AccessDenied)]
    [InlineData(LobbyError.InvalidMetadata, UiLobbyError.InvalidMetadata)]
    [InlineData(LobbyError.IncompatibleBuild, UiLobbyError.IncompatibleBuild)]
    [InlineData(LobbyError.OwnerChanged, UiLobbyError.OwnerChanged)]
    [InlineData(LobbyError.Cancelled, UiLobbyError.Cancelled)]
    [InlineData(LobbyError.PlatformFailure, UiLobbyError.PlatformFailure)]
    public void MapError_CoversEveryControllerError(LobbyError error, UiLobbyError expected)
    {
        Assert.Equal(expected, SteamUiLobbyService.MapError(error));
    }

    [Fact]
    public void MapState_CoversEveryControllerState()
    {
        foreach (var state in Enum.GetValues<LobbyState>())
        {
            Assert.Equal(state.ToString(), SteamUiLobbyService.MapState(state).ToString());
        }
    }

    [Theory]
    [InlineData(UiLobbyVisibility.Private, LobbyVisibility.Private)]
    [InlineData(UiLobbyVisibility.FriendsOnly, LobbyVisibility.FriendsOnly)]
    [InlineData(UiLobbyVisibility.Public, LobbyVisibility.Public)]
    public void Host_MapsVisibilityAndCapacity(UiLobbyVisibility visibility, LobbyVisibility expected)
    {
        using var f = new LobbyFixture();
        using var ui = new SteamUiLobbyService(f.Controller);
        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));

        ui.Host(visibility, 6);

        var request = Assert.Single(f.Api.CreateRequests);
        Assert.Equal(expected, request.Visibility);
        Assert.Equal(6, request.Capacity);
        Assert.Equal(UiLobbyState.Creating, ui.State);
        Assert.Equal(expected, SteamUiLobbyService.MapVisibility(visibility));
    }

    [Fact]
    public void Invite_DuringHosting_OpensTheOverlayOfTheLobby()
    {
        using var f = new LobbyFixture();
        using var ui = new SteamUiLobbyService(f.Controller);
        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
        f.Controller.Host(LobbyVisibility.Private, 4);
        var lobby = f.Api.CompleteCreate();

        ui.Invite();

        Assert.Equal([lobby], f.Api.Overlays);
    }

    [Fact]
    public void Friends_VisibleRefreshesAndRaisesChangedAndCopies()
    {
        using var f = new LobbyFixture();
        using var ui = new SteamUiLobbyService(f.Controller);
        var changed = 0;
        ui.FriendsChanged += () => changed++;
        f.Api.FriendsInGames.Add((30, "bravo", 480, 1));
        f.Api.FriendsInGames.Add((20, "alpha", 480, 2));

        ui.SetFriendsVisible(true);

        Assert.Equal(1, changed);
        var into = new UiLobbyFriend[4].AsSpan();
        var count = ui.CopyFriends(into);
        Assert.Equal(2, count);
        Assert.Equal(new UiLobbyFriend(20, "alpha", 2), into[0]);
        Assert.Equal(new UiLobbyFriend(30, "bravo", 1), into[1]);
        Assert.Equal(1, ui.CopyFriends(into[..1]));
    }

    [Fact]
    public void InviteDuringSession_ConfirmThenSessionEnds_JoinsThroughTheController()
    {
        using var f = new LobbyFixture { SessionActive = true };
        using var ui = new SteamUiLobbyService(f.Controller);
        var asked = new List<ulong>();
        var connects = new List<ulong>();
        ui.JoinConfirmationRequested += asked.Add;
        ui.ConnectRequested += (lobby, _) => connects.Add(lobby);
        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);

        f.Api.RaiseJoinRequested(5000);
        ui.ConfirmJoin(5000);
        f.SessionActive = false;
        f.Controller.Update();
        f.Api.RaiseJoined(new LobbyApiResult(f.Api.JoinRequests[^1].RequestId, 5000, LobbyError.None));

        Assert.Equal([5000UL], asked);
        Assert.Equal([5000UL], connects);
    }

    [Fact]
    public void DeclineJoin_DropsThePendingInvite()
    {
        using var f = new LobbyFixture { SessionActive = true };
        using var ui = new SteamUiLobbyService(f.Controller);
        f.Api.RaiseJoinRequested(5000);

        ui.DeclineJoin();
        ui.ConfirmJoin(5000);
        f.SessionActive = false;
        f.Controller.Update();

        Assert.Empty(f.Api.JoinRequests);
    }

    [Fact]
    public void Dispose_StopsForwarding()
    {
        using var f = new LobbyFixture();
        var ui = new SteamUiLobbyService(f.Controller);
        var states = new List<UiLobbyState>();
        ui.StateChanged += states.Add;

        ui.Dispose();
        f.Controller.SetUnavailable();

        Assert.Empty(states);
    }
}
