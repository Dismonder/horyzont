using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

/// <summary>K5-26 decision 8 (X5): <c>ShipStatus</c> only from the server, in Loading and InGame, on the unreliable channel.</summary>
public sealed class ShipStatusMessageRouterTests
{
    [Fact]
    public void MessageRouter_ShipStatus_ServerInLoadingAndInGame()
    {
        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, MessageType.ShipStatus));
        }

        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(state is ClientState.Loading or ClientState.InGame, MessageRouter.IsAllowedFromServer(state, MessageType.ShipStatus));
        }

        Assert.True(MessageRouter.IsGameMessageFromServer(MessageType.ShipStatus));
        Assert.False(MessageRouter.IsGameMessageFromClient(MessageType.ShipStatus));
        Assert.True(MessageRouter.TryGetType([31], out var type));
        Assert.Equal(MessageType.ShipStatus, type);
        Assert.Equal(DeliveryChannel.Unreliable, MessageChannels.For(MessageType.ShipStatus));
    }
}
