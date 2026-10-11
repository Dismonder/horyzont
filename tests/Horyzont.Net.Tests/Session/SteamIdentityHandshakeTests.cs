using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests.Session;

public sealed class SteamIdentityHandshakeTests
{
    [Fact]
    public void Handshake_SteamIdentityOverUdp_IsRejected()
    {
        using var h = new SessionHarness();
        var claimed = new PlatformIdentity(IdentityPlatform.Steam, 76561198000000001UL);

        // Without a fake SteamID the harness client connects through a non-Steam endpoint, like UDP.
        var client = h.AddClient(claimed);

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));
        Assert.Equal(HandshakeRejectCode.IdentityMismatch, Assert.Single(client.Hooks.Rejections).Code);
        Assert.Empty(h.ServerHooks.Joined);
        Assert.Empty(h.Server.PlayersInGame);
    }

    [Fact]
    public void Handshake_ForgedP2PIdentity_DoesNotApplyPlayerJoin()
    {
        using var h = new SessionHarness();

        var client = h.AddClient(new PlatformIdentity(IdentityPlatform.Steam, 222), fakeSteamId: 111);

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(5)));
        Assert.Equal(HandshakeRejectCode.IdentityMismatch, Assert.Single(client.Hooks.Rejections).Code);
        Assert.Empty(h.ServerHooks.Joined);
        Assert.Empty(h.ServerHooks.Calls);
        Assert.Empty(h.Server.PlayersInGame);
    }

    [Fact]
    public void Handshake_SteamIdentityOverMatchingP2PEndpoint_IsAccepted()
    {
        using var h = new SessionHarness();

        var client = h.AddClient(new PlatformIdentity(IdentityPlatform.Steam, 111), fakeSteamId: 111);

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Loading, TimeSpan.FromSeconds(5)));
        Assert.Empty(client.Hooks.Rejections);
    }

    [Fact]
    public void Handshake_DevIdentityOverInMemory_IsStillAccepted()
    {
        using var h = new SessionHarness();

        var client = h.JoinInGame();

        Assert.Equal(ClientState.InGame, client.Session.State);
    }
}
