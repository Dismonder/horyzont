using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Net.Tests.Session;

namespace Horyzont.Net.Tests;

public sealed class JoinUnavailableTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Join_ReadOnlyServer_ReceivesJoinUnavailableWithoutPlayerJoin(byte reason)
    {
        using var h = new SessionHarness();
        h.ServerHooks.JoinFailure = new JoinUnavailableException(reason, "read only");
        var client = h.AddClient();
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(2)));
        var message = Assert.IsType<JoinUnavailable>(Assert.Single(client.Hooks.Messages));
        Assert.Equal(reason, message.Reason);
        Assert.Null(client.Hooks.Hello);
        Assert.Empty(h.ServerHooks.Joined);
        Assert.Empty(h.ServerHooks.Left);
        Assert.DoesNotContain("CreateHello", h.ServerHooks.Calls);
    }

    [Fact]
    public void Join_WrongBuildStillUsesFrozenHandshakeReject()
    {
        using var h = new SessionHarness();
        h.ServerHooks.JoinFailure = new JoinUnavailableException(2, "read only");
        var client = h.AddClient(buildHash: SessionHarness.Build + 1);
        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, TimeSpan.FromSeconds(2)));
        Assert.Single(client.Hooks.Rejections);
        Assert.Empty(client.Hooks.Messages);
        Assert.Empty(h.ServerHooks.Calls);
    }

    [Fact]
    public void Router_StatusAndJoinUnavailable_OnlyAllowsAssignedDirectionAndStates()
    {
        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(state is ClientState.Loading or ClientState.InGame, MessageRouter.IsAllowedFromServer(state, MessageType.ServerStatus));
            Assert.Equal(state == ClientState.AwaitingServerHello, MessageRouter.IsAllowedFromServer(state, MessageType.JoinUnavailable));
        }
        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, MessageType.ServerStatus));
            Assert.False(MessageRouter.IsAllowedFromClient(state, MessageType.JoinUnavailable));
        }
        Assert.False(MessageRouter.IsGameMessageFromServer(MessageType.JoinUnavailable));
        Assert.True(MessageRouter.IsGameMessageFromServer(MessageType.ServerStatus));
        Assert.True(MessageRouter.TryGetType(new byte[] {22}, out _));
        Assert.True(MessageRouter.TryGetType(new byte[] {23}, out _));
    }
}
