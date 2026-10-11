using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

public sealed class StarSystemMessageRouterTests
{
    [Fact]
    public void MessageRouter_StarSystemState_ServerOnlyLoadingAndInGame()
    {
        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, MessageType.StarSystemState));
        }

        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(state is ClientState.Loading or ClientState.InGame, MessageRouter.IsAllowedFromServer(state, MessageType.StarSystemState));
        }

        Assert.True(MessageRouter.IsGameMessageFromServer(MessageType.StarSystemState));
        Assert.False(MessageRouter.IsGameMessageFromClient(MessageType.StarSystemState));
        Assert.True(MessageRouter.TryGetType([32], out var type));
        Assert.Equal(MessageType.StarSystemState, type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(MessageType.StarSystemState));
    }
}
