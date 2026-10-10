using Horyzont.Client.Steam;

namespace Horyzont.Client.Tests.Steam;

public sealed class LobbyHostingTests
{
    [Theory]
    [InlineData(LobbyVisibility.Private)]
    [InlineData(LobbyVisibility.FriendsOnly)]
    [InlineData(LobbyVisibility.Public)]
    public void Host_ThreeVisibilityModes_WritesMetadataAndReadyLast(LobbyVisibility visibility)
    {
        using var f = new LobbyFixture();
        var world = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToString("D");
        f.Controller.SetSession(new LobbySessionInfo(world, true));

        f.Controller.Host(visibility, 8);

        Assert.Equal(LobbyState.Creating, f.Controller.State);
        var request = Assert.Single(f.Api.CreateRequests);
        Assert.Equal(visibility, request.Visibility);
        Assert.Equal(8, request.Capacity);
        var lobby = f.Api.CompleteCreate();

        Assert.Equal(LobbyState.Hosting, f.Controller.State);
        string[] expected =
        [
            $"set:{lobby}:horyzont_schema=1",
            $"set:{lobby}:horyzont_host={f.Api.LocalSteamId}",
            $"set:{lobby}:horyzont_build={LobbyFixture.Build}",
            $"set:{lobby}:horyzont_protocol={LobbyFixture.Protocol}",
            $"set:{lobby}:horyzont_world={world}",
            $"set:{lobby}:horyzont_capacity=8",
            $"set:{lobby}:horyzont_password=1",
            $"set:{lobby}:horyzont_ready=1",
        ];
        Assert.Equal(expected, f.Api.Calls.Where(c => c.StartsWith("set:", StringComparison.Ordinal)).ToArray());
        Assert.Equal(
            new LobbyInfo(lobby, f.Api.LocalSteamId, visibility, 1, 8, LobbyFixture.Build, LobbyFixture.Protocol),
            f.Controller.Current);
        Assert.DoesNotContain(f.Api.Calls, c => c.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Host_WithoutWorldFacts_FailsWithoutCreatingLobby()
    {
        using var f = new LobbyFixture();

        f.Controller.Host(LobbyVisibility.FriendsOnly, 8);

        Assert.Empty(f.Api.CreateRequests);
        Assert.Equal(LobbyState.Failed, f.Controller.State);
        Assert.Equal([LobbyError.InvalidMetadata], f.Errors);
    }

    [Theory]
    [InlineData(LobbyError.PlatformFailure)]
    [InlineData(LobbyError.Timeout)]
    [InlineData(LobbyError.AccessDenied)]
    public void Host_CreateFails_ReportsFailedAndAllowsRetry(LobbyError error)
    {
        using var f = new LobbyFixture();
        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
        f.Controller.Host(LobbyVisibility.FriendsOnly, 8);

        f.Api.RaiseCreated(new LobbyApiResult(f.Api.CreateRequests[0].RequestId, 0, error));

        Assert.Equal(LobbyState.Failed, f.Controller.State);
        Assert.Equal([error], f.Errors);
        Assert.Null(f.Controller.Current);

        f.Controller.Host(LobbyVisibility.FriendsOnly, 8);
        Assert.Equal(LobbyState.Creating, f.Controller.State);
        Assert.Equal(2, f.Api.CreateRequests.Count);
    }

    [Fact]
    public void Host_MetadataWriteFails_LeavesAndFails()
    {
        using var f = new LobbyFixture();
        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
        f.Controller.Host(LobbyVisibility.Public, 8);
        f.Api.SetDataResult = false;

        var lobby = f.Api.CompleteCreate();

        Assert.Equal([lobby], f.Api.Left);
        Assert.Equal(LobbyState.Failed, f.Controller.State);
        Assert.Equal([LobbyError.PlatformFailure], f.Errors);
        Assert.DoesNotContain(f.Api.Calls, c => c.EndsWith("horyzont_ready=1", StringComparison.Ordinal));
    }

    [Fact]
    public void Host_LateCreateAfterLeave_LeavesTheStaleLobby()
    {
        using var f = new LobbyFixture();
        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
        f.Controller.Host(LobbyVisibility.Public, 8);

        f.Controller.Leave();
        var lobby = f.Api.CompleteCreate();

        Assert.Equal(LobbyState.Idle, f.Controller.State);
        Assert.Equal([lobby], f.Api.Left);
        Assert.DoesNotContain(f.Api.Calls, c => c.StartsWith("set:", StringComparison.Ordinal));
    }

    [Fact]
    public void Leave_Hosting_IsIdempotentAndMarksNotReadyBeforeLeaving()
    {
        using var f = new LobbyFixture();
        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
        f.Controller.Host(LobbyVisibility.Public, 8);
        var lobby = f.Api.CompleteCreate();

        f.Controller.PrepareShutdown();
        f.Controller.PrepareShutdown();
        f.Controller.Leave();
        f.Controller.Leave();

        Assert.Equal($"set:{lobby}:horyzont_ready=0", f.Api.Calls[^2]);
        Assert.Equal($"leave:{lobby}", f.Api.Calls[^1]);
        Assert.Single(f.Api.Left);
        Assert.Equal(LobbyState.Idle, f.Controller.State);
        Assert.Null(f.Controller.Current);
    }

    [Fact]
    public void Owner_ChangedDuringSession_EndsWithOwnerChanged()
    {
        using var f = new LobbyFixture();
        var lobby = f.JoinValid();
        Assert.Equal(LobbyState.Joined, f.Controller.State);

        f.Api.SetOwner(lobby, 999);
        f.Time.Advance(TimeSpan.FromMilliseconds(900));
        f.Controller.Update();
        Assert.Equal(LobbyState.Joined, f.Controller.State);
        f.Time.Advance(TimeSpan.FromMilliseconds(200));
        f.Controller.Update();

        Assert.Equal(LobbyState.Failed, f.Controller.State);
        Assert.Equal([LobbyError.OwnerChanged], f.Errors);
        Assert.Equal([lobby], f.Api.Left);
        Assert.Single(f.Connects);
    }

    [Fact]
    public void Owner_UnchangedForManySeconds_StaysJoined()
    {
        using var f = new LobbyFixture();
        f.JoinValid();

        for (var i = 0; i < 20; i++)
        {
            f.Time.Advance(TimeSpan.FromSeconds(1));
            f.Controller.Update();
        }

        Assert.Equal(LobbyState.Joined, f.Controller.State);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void Invite_InactiveLobby_DoesNotOpenOverlay()
    {
        using var f = new LobbyFixture();
        f.Controller.Invite();
        Assert.Empty(f.Api.Overlays);

        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
        f.Controller.Host(LobbyVisibility.Public, 8);
        f.Controller.Invite();
        Assert.Empty(f.Api.Overlays);

        f.Api.RaiseCreated(new LobbyApiResult(f.Api.CreateRequests[0].RequestId, 0, LobbyError.PlatformFailure));
        f.Controller.Invite();
        Assert.Empty(f.Api.Overlays);

        f.Controller.Host(LobbyVisibility.Public, 8);
        var lobby = f.Api.CompleteCreate();
        f.Controller.Invite();
        Assert.Equal([lobby], f.Api.Overlays);

        f.Controller.Leave();
        f.Controller.Invite();
        Assert.Equal([lobby], f.Api.Overlays);
    }

    [Fact]
    public void Friends_OtherAppAndNoLobby_FiltersAndSorts()
    {
        using var f = new LobbyFixture();
        f.Api.TryInitialize(480);
        f.Api.FriendsInGames.Add((30, "bravo", 480, 1));
        f.Api.FriendsInGames.Add((20, "alpha", 480, 2));
        f.Api.FriendsInGames.Add((10, "alpha", 480, 3));
        f.Api.FriendsInGames.Add((40, "other game", 999, 4));
        f.Api.FriendsInGames.Add((50, "no lobby", 480, 0));
        var refreshed = 0;
        f.Controller.FriendsChanged += () => refreshed++;

        f.Controller.SetFriendsVisible(true);

        Assert.Equal(
            [new SteamFriend(10, "alpha", 3), new SteamFriend(20, "alpha", 2), new SteamFriend(30, "bravo", 1)],
            f.Controller.Friends);
        Assert.Equal(1, refreshed);

        f.Api.FriendsInGames.Add((5, "aardvark", 480, 9));
        f.Time.Advance(TimeSpan.FromSeconds(4));
        f.Controller.Update();
        Assert.Equal(3, f.Controller.Friends.Count);
        f.Time.Advance(TimeSpan.FromSeconds(1));
        f.Controller.Update();
        Assert.Equal(4, f.Controller.Friends.Count);
        Assert.Equal("aardvark", f.Controller.Friends[0].Name);
        Assert.Equal(2, refreshed);

        f.Controller.SetFriendsVisible(false);
        f.Api.FriendsInGames.Add((6, "hidden", 480, 10));
        f.Time.Advance(TimeSpan.FromSeconds(30));
        f.Controller.Update();
        Assert.Equal(4, f.Controller.Friends.Count);
    }

    [Fact]
    public void Friends_MoreThanLimit_IsCappedAt256()
    {
        using var f = new LobbyFixture();
        for (var i = 1; i <= 300; i++)
        {
            f.Api.FriendsInGames.Add(((ulong)i, "friend" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), 480, (ulong)i));
        }

        f.Controller.SetFriendsVisible(true);

        Assert.Equal(256, f.Controller.Friends.Count);
        Assert.Equal(f.Controller.Friends.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.SteamId), f.Controller.Friends);
    }

    [Fact]
    public void Dispose_RemovesApiSubscriptions()
    {
        var f = new LobbyFixture();
        Assert.True(f.Api.SubscriberCount > 0);

        f.Dispose();

        Assert.Equal(0, f.Api.SubscriberCount);
    }
}
