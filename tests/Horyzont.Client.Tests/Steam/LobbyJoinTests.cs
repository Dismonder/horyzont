using Horyzont.Client.Steam;
using Horyzont.Net.Transport;

namespace Horyzont.Client.Tests.Steam;

public sealed class LobbyJoinTests
{
    [Fact]
    public void Join_ValidLobby_ConnectsToOwnerNotLobbyId()
    {
        using var f = new LobbyFixture();

        var lobby = f.JoinValid(lobbyId: 5000);

        Assert.NotEqual(LobbyFixture.HostId, lobby);
        var connect = Assert.Single(f.Connects);
        Assert.Equal(new SteamEndpoint(LobbyFixture.HostId), connect.Endpoint);
        Assert.False(connect.Password);
        Assert.Equal(LobbyState.Joined, f.Controller.State);
        Assert.Equal(lobby, f.Controller.Current!.Value.LobbyId);
        Assert.Equal(LobbyFixture.HostId, f.Controller.Current!.Value.HostSteamId);
        Assert.Equal(8, f.Controller.Current!.Value.Capacity);
        Assert.Equal([LobbyState.Joining, LobbyState.Joined], f.States);
    }

    [Fact]
    public void Join_PasswordProtectedLobby_RequestsPassword()
    {
        using var f = new LobbyFixture();

        f.JoinValid(password: true);

        var connect = Assert.Single(f.Connects);
        Assert.True(connect.Password);
        Assert.Equal(new SteamEndpoint(LobbyFixture.HostId), connect.Endpoint);
        Assert.DoesNotContain(f.Api.Calls, c => c.StartsWith("set:", StringComparison.Ordinal));
    }

    public static TheoryData<string, string?, LobbyError> BadMetadata => new()
    {
        { LobbyMetadata.Schema, null, LobbyError.InvalidMetadata },
        { LobbyMetadata.Schema, "2", LobbyError.InvalidMetadata },
        { LobbyMetadata.Host, null, LobbyError.InvalidMetadata },
        { LobbyMetadata.Host, "0", LobbyError.InvalidMetadata },
        { LobbyMetadata.Host, "abc", LobbyError.InvalidMetadata },
        { LobbyMetadata.Host, "123", LobbyError.InvalidMetadata },
        { LobbyMetadata.Build, null, LobbyError.InvalidMetadata },
        { LobbyMetadata.Build, "1234", LobbyError.IncompatibleBuild },
        { LobbyMetadata.Protocol, null, LobbyError.InvalidMetadata },
        { LobbyMetadata.Protocol, "x", LobbyError.InvalidMetadata },
        { LobbyMetadata.Protocol, "4", LobbyError.IncompatibleBuild },
        { LobbyMetadata.World, null, LobbyError.InvalidMetadata },
        { LobbyMetadata.World, "not-a-guid", LobbyError.InvalidMetadata },
        { LobbyMetadata.Capacity, null, LobbyError.InvalidMetadata },
        { LobbyMetadata.Capacity, "0", LobbyError.InvalidMetadata },
        { LobbyMetadata.Capacity, "-3", LobbyError.InvalidMetadata },
        { LobbyMetadata.Capacity, "100000", LobbyError.InvalidMetadata },
        { LobbyMetadata.Password, null, LobbyError.InvalidMetadata },
        { LobbyMetadata.Password, "2", LobbyError.InvalidMetadata },
        { LobbyMetadata.Ready, null, LobbyError.InvalidMetadata },
        { LobbyMetadata.Ready, "0", LobbyError.InvalidMetadata },
        { LobbyMetadata.World, new string('x', 129), LobbyError.InvalidMetadata },
        { LobbyMetadata.Build, new string('9', 129), LobbyError.InvalidMetadata },
    };

    [Theory]
    [MemberData(nameof(BadMetadata))]
    public void Join_BadMetadataOrDifferentBuild_DoesNotConnect(string key, string? value, LobbyError expected)
    {
        using var f = new LobbyFixture();
        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);
        f.Api.Corrupt(5000, key, value);
        f.Controller.Join(5000);

        f.Api.RaiseJoined(new LobbyApiResult(f.Api.JoinRequests[0].RequestId, 5000, LobbyError.None));

        Assert.Empty(f.Connects);
        Assert.Equal([expected], f.Errors);
        Assert.Equal(LobbyState.Failed, f.Controller.State);
        Assert.Equal([5000UL], f.Api.Left);
    }

    [Fact]
    public void Join_OwnerDiffersFromPublishedHost_DoesNotConnect()
    {
        using var f = new LobbyFixture();
        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);
        f.Api.SetOwner(5000, 12345);
        f.Controller.Join(5000);

        f.Api.RaiseJoined(new LobbyApiResult(f.Api.JoinRequests[0].RequestId, 5000, LobbyError.None));

        Assert.Empty(f.Connects);
        Assert.Equal([LobbyError.InvalidMetadata], f.Errors);
    }

    [Fact]
    public void Join_ZeroLobbyId_FailsWithoutCallingSteam()
    {
        using var f = new LobbyFixture();

        f.Controller.Join(0);

        Assert.Empty(f.Api.JoinRequests);
        Assert.Equal([LobbyError.InvalidMetadata], f.Errors);
        Assert.Equal(LobbyState.Failed, f.Controller.State);
    }

    [Theory]
    [InlineData(LobbyError.Full)]
    [InlineData(LobbyError.AccessDenied)]
    [InlineData(LobbyError.Unavailable)]
    [InlineData(LobbyError.PlatformFailure)]
    public void Join_FullDeniedAndTimeout_ReturnsExpectedError(LobbyError error)
    {
        using var f = new LobbyFixture();
        f.Controller.Join(5000);

        f.Api.RaiseJoined(new LobbyApiResult(f.Api.JoinRequests[0].RequestId, 0, error));

        Assert.Equal([error], f.Errors);
        Assert.Equal(LobbyState.Failed, f.Controller.State);
        Assert.Empty(f.Connects);
    }

    [Fact]
    public void Join_NoAnswerFor10Seconds_FailsWithTimeoutAndRetryIsPossible()
    {
        using var f = new LobbyFixture();
        f.Controller.Join(5000);

        f.Time.Advance(TimeSpan.FromSeconds(9.9));
        f.Controller.Update();
        Assert.Equal(LobbyState.Joining, f.Controller.State);
        f.Time.Advance(TimeSpan.FromSeconds(0.2));
        f.Controller.Update();

        Assert.Equal([LobbyError.Timeout], f.Errors);
        Assert.Equal(LobbyState.Failed, f.Controller.State);

        f.Api.PublishLobby(5001, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);
        f.Controller.Join(5001);
        Assert.Equal(LobbyState.Joining, f.Controller.State);
        Assert.Equal(2, f.Api.JoinRequests.Count);
    }

    [Fact]
    public void Host_CreateNoAnswerFor10Seconds_FailsWithTimeout()
    {
        using var f = new LobbyFixture();
        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
        f.Controller.Host(LobbyVisibility.Public, 8);

        f.Time.Advance(TimeSpan.FromSeconds(10));
        f.Controller.Update();

        Assert.Equal([LobbyError.Timeout], f.Errors);
        var late = f.Api.CompleteCreate();
        Assert.Equal([late], f.Api.Left);
    }

    [Fact]
    public void Join_CancelThenLateSuccess_LeavesWithoutConnecting()
    {
        using var f = new LobbyFixture();
        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);
        f.Controller.Join(5000);
        var first = f.Api.JoinRequests[0].RequestId;

        f.Controller.Leave();
        Assert.Equal(LobbyState.Idle, f.Controller.State);
        f.Controller.Join(5000);
        var second = f.Api.JoinRequests[1].RequestId;
        Assert.NotEqual(first, second);

        f.Api.RaiseJoined(new LobbyApiResult(first, 5000, LobbyError.None));
        Assert.Empty(f.Connects);
        Assert.Equal(LobbyState.Joining, f.Controller.State);
        Assert.Equal([5000UL], f.Api.Left);

        f.Api.RaiseJoined(new LobbyApiResult(second, 5000, LobbyError.None));
        Assert.Single(f.Connects);
        Assert.Equal(LobbyState.Joined, f.Controller.State);

        // A duplicate callback of the winning request must neither connect again nor leave the live lobby.
        f.Api.RaiseJoined(new LobbyApiResult(second, 5000, LobbyError.None));
        Assert.Single(f.Connects);
        Assert.Equal([5000UL], f.Api.Left);
        Assert.Equal(LobbyState.Joined, f.Controller.State);
    }

    [Fact]
    public void Join_WhileBusy_IsIgnored()
    {
        using var f = new LobbyFixture();
        f.Controller.Join(5000);
        f.Controller.Join(5001);
        Assert.Single(f.Api.JoinRequests);

        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);
        f.Api.RaiseJoined(new LobbyApiResult(f.Api.JoinRequests[0].RequestId, 5000, LobbyError.None));
        f.Controller.Join(5002);
        Assert.Single(f.Api.JoinRequests);
    }

    [Fact]
    public void Join_AfterLeaveOfJoinedLobby_StartsNewJoin()
    {
        using var f = new LobbyFixture();
        f.JoinValid();
        f.Controller.Leave();
        Assert.Equal(LobbyState.Idle, f.Controller.State);
        Assert.Equal([5000UL], f.Api.Left);

        f.Controller.Join(5001);

        Assert.Equal(2, f.Api.JoinRequests.Count);
    }

    [Fact]
    public void JoinRequest_NoSession_JoinsImmediately()
    {
        using var f = new LobbyFixture();

        f.Api.RaiseJoinRequested(5000);

        Assert.Empty(f.Confirmations);
        Assert.Equal(5000UL, Assert.Single(f.Api.JoinRequests).LobbyId);
    }

    [Fact]
    public void JoinRequest_ActiveWorld_WaitsForConfirmationAndShutdown()
    {
        using var f = new LobbyFixture();
        f.SessionActive = true;
        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);

        f.Api.RaiseJoinRequested(4000);
        f.Api.RaiseJoinRequested(5000);

        Assert.Equal([4000UL, 5000UL], f.Confirmations);
        Assert.Empty(f.Api.JoinRequests);

        // Decline keeps the world and drops the request.
        f.Controller.DeclineJoin();
        f.Controller.ConfirmJoin(5000);
        f.Controller.Update();
        Assert.Empty(f.Api.JoinRequests);

        // Confirm only waits for the session release; the old session keeps running until then.
        f.Api.RaiseJoinRequested(5000);
        f.Controller.ConfirmJoin(5000);
        f.Controller.Update();
        Assert.Empty(f.Api.JoinRequests);

        f.SessionActive = false;
        f.Controller.Update();
        Assert.Equal(5000UL, Assert.Single(f.Api.JoinRequests).LobbyId);
        f.Controller.Update();
        Assert.Single(f.Api.JoinRequests);
    }

    [Fact]
    public void JoinRequest_ActiveWorldWithoutSubscriber_IsDeclined()
    {
        var api = new Horyzont.Testing.FakeSteamLobbyApi();
        using var controller = new SteamLobbyController(
            api, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), () => true,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SteamLobbyController>.Instance);
        controller.Configure("1", 3);

        api.RaiseJoinRequested(5000);
        controller.Update();

        Assert.Empty(api.JoinRequests);
    }

    [Fact]
    public void ConfirmedJoin_FromHostingLobby_LeavesTheOwnLobbyFirst()
    {
        using var f = new LobbyFixture();
        f.Controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
        f.Controller.Host(LobbyVisibility.Public, 8);
        var own = f.Api.CompleteCreate();
        f.SessionActive = true;
        f.Api.PublishLobby(5000, LobbyFixture.HostId, LobbyFixture.Build, LobbyFixture.Protocol);
        f.Api.RaiseJoinRequested(5000);
        f.Controller.ConfirmJoin(5000);

        f.SessionActive = false;
        f.Controller.Update();

        Assert.Contains(own, f.Api.Left);
        Assert.Equal(5000UL, Assert.Single(f.Api.JoinRequests).LobbyId);
    }

    [Fact]
    public void Unavailable_AnyOperation_FailsWithUnavailable()
    {
        using var f = new LobbyFixture();
        f.Controller.SetUnavailable();

        f.Controller.Join(5000);
        f.Controller.Host(LobbyVisibility.Public, 8);

        Assert.Equal(LobbyState.Unavailable, f.Controller.State);
        Assert.Equal([LobbyError.Unavailable, LobbyError.Unavailable], f.Errors);
        Assert.Empty(f.Api.JoinRequests);
        Assert.Empty(f.Api.CreateRequests);
    }
}
