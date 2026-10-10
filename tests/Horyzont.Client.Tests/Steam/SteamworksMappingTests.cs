using Horyzont.Client.Steam;

using Steamworks;

namespace Horyzont.Client.Tests.Steam;

/// <summary>Pure translation tables of the Steamworks adapter (no SDK call): they pin the names confirmed against Steamworks.NET 2024.8.0.</summary>
public sealed class SteamworksMappingTests
{
    [Theory]
    [InlineData(LobbyVisibility.Private, ELobbyType.k_ELobbyTypePrivate)]
    [InlineData(LobbyVisibility.FriendsOnly, ELobbyType.k_ELobbyTypeFriendsOnly)]
    [InlineData(LobbyVisibility.Public, ELobbyType.k_ELobbyTypePublic)]
    public void ToLobbyType_AllVisibilities_MapsToSdkType(LobbyVisibility visibility, ELobbyType expected) =>
        Assert.Equal(expected, SteamworksLobbyApi.ToLobbyType(visibility));

    [Theory]
    [InlineData(EResult.k_EResultOK, false, LobbyError.None)]
    [InlineData(EResult.k_EResultTimeout, false, LobbyError.Timeout)]
    [InlineData(EResult.k_EResultAccessDenied, false, LobbyError.AccessDenied)]
    [InlineData(EResult.k_EResultFail, false, LobbyError.PlatformFailure)]
    [InlineData(EResult.k_EResultOK, true, LobbyError.PlatformFailure)]
    public void MapCreateResult_SdkResult_TranslatesToLobbyError(EResult result, bool ioFailure, LobbyError expected) =>
        Assert.Equal(expected, SteamworksLobbyApi.MapCreateResult(result, ioFailure));

    [Theory]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess, false, LobbyError.None)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseFull, false, LobbyError.Full)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist, false, LobbyError.Unavailable)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed, false, LobbyError.AccessDenied)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned, false, LobbyError.AccessDenied)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited, false, LobbyError.AccessDenied)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseClanDisabled, false, LobbyError.AccessDenied)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseCommunityBan, false, LobbyError.AccessDenied)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou, false, LobbyError.AccessDenied)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember, false, LobbyError.AccessDenied)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseError, false, LobbyError.PlatformFailure)]
    [InlineData(EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess, true, LobbyError.PlatformFailure)]
    public void MapEnterResponse_SdkResponse_TranslatesToLobbyError(EChatRoomEnterResponse response, bool ioFailure, LobbyError expected) =>
        Assert.Equal(expected, SteamworksLobbyApi.MapEnterResponse((uint)response, ioFailure));

    [Theory]
    [InlineData(480u, 7UL, 480u, true)]
    [InlineData(480u, 0UL, 480u, false)]
    [InlineData(999u, 7UL, 480u, false)]
    public void IsJoinable_AppAndLobby_FiltersOtherGamesAndLobbylessFriends(uint friendApp, ulong lobby, uint app, bool expected) =>
        Assert.Equal(expected, SteamworksLobbyApi.IsJoinable(friendApp, lobby, app));
}
