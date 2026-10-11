using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

public sealed class MarkersMessageRouterTests
{
    [Fact]
    public void MessageRouter_MarkersState_ServerOnlyLoadingAndInGame()
    {
        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, MessageType.MarkersState));
        }

        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(state is ClientState.Loading or ClientState.InGame, MessageRouter.IsAllowedFromServer(state, MessageType.MarkersState));
        }

        Assert.True(MessageRouter.IsGameMessageFromServer(MessageType.MarkersState));
        Assert.False(MessageRouter.IsGameMessageFromClient(MessageType.MarkersState));
        Assert.True(MessageRouter.TryGetType([33], out var type));
        Assert.Equal(MessageType.MarkersState, type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(MessageType.MarkersState));
    }
}
