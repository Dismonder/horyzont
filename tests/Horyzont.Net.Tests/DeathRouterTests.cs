using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

/// <summary>Routing of <c>WorldSettingsState</c> (message 47, K6-35 D4): sent by the server like the state of the containers, never by a client.</summary>
public sealed class DeathRouterTests
{
    [Fact]
    public void MessageRouter_WorldSettingsState_AllowedFromServerLikeProgressState()
    {
        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(MessageRouter.IsAllowedFromServer(state, MessageType.PlayerProgressState), MessageRouter.IsAllowedFromServer(state, MessageType.WorldSettingsState));
        }

        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, MessageType.WorldSettingsState));
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.Loading, MessageType.WorldSettingsState));
        Assert.False(MessageRouter.IsAllowedFromServer(ClientState.AwaitingServerHello, MessageType.WorldSettingsState));
        Assert.True(MessageRouter.IsGameMessageFromServer(MessageType.WorldSettingsState));
    }

    [Fact]
    public void MessageRouter_WorldSettingsState_NeverAllowedFromClient()
    {
        // The control: the server may send it, so a refusal in the other direction is a rule and not an unknown type.
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, MessageType.WorldSettingsState));

        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, MessageType.WorldSettingsState));
        }

        Assert.False(MessageRouter.IsGameMessageFromClient(MessageType.WorldSettingsState));
    }

    [Fact]
    public void MessageRouter_WorldSettingsState_TypeByteIsKnownAndReliable()
    {
        Assert.True(MessageRouter.TryGetType([47], out var type));
        Assert.Equal(MessageType.WorldSettingsState, type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(MessageType.WorldSettingsState));
    }
}
