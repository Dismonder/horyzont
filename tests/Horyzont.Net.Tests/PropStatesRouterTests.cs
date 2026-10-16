using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

/// <summary>
/// Routing of <c>PropStates</c> (message 45, K6-31 D17): the server sends it in the states in which it sends
/// <c>BlockEntityStates</c>; a client never sends it.
/// </summary>
public sealed class PropStatesRouterTests
{
    private const MessageType PropStatesType = MessageType.PropStates;

    private const MessageType BlockEntityStatesType = MessageType.BlockEntityStates;

    [Fact]
    public void MessageRouter_PropStates_AllowedFromServerLikeBlockEntityStates()
    {
        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(MessageRouter.IsAllowedFromServer(state, BlockEntityStatesType), MessageRouter.IsAllowedFromServer(state, PropStatesType));
        }

        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.Loading, PropStatesType));
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, PropStatesType));
        Assert.False(MessageRouter.IsAllowedFromServer(ClientState.AwaitingServerHello, PropStatesType));
        Assert.True(MessageRouter.IsGameMessageFromServer(PropStatesType));
    }

    [Fact]
    public void MessageRouter_PropStates_NeverAllowedFromClient()
    {
        // The control: the server may send it, so a refusal in the other direction is a rule and not an unknown type.
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, PropStatesType));

        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, PropStatesType));
        }

        Assert.False(MessageRouter.IsGameMessageFromClient(PropStatesType));
    }

    [Fact]
    public void MessageRouter_PropStates_TypeByteIsKnownAndReliable()
    {
        Assert.True(MessageRouter.TryGetType([45], out var type));
        Assert.Equal(PropStatesType, type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(PropStatesType));
    }
}
