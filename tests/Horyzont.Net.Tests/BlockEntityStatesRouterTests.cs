using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

/// <summary>
/// Routing of <c>BlockEntityStates</c> (message 46, K6-30 D15): the server sends it in the states in which it sends
/// <see cref="MessageType.ConstructState"/>; a client never sends it.
/// </summary>
public sealed class BlockEntityStatesRouterTests
{
    private const MessageType BlockEntityStatesType = MessageType.BlockEntityStates;

    [Fact]
    public void MessageRouter_BlockEntityStates_AllowedFromServerLikeConstructState()
    {
        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(MessageRouter.IsAllowedFromServer(state, MessageType.ConstructState), MessageRouter.IsAllowedFromServer(state, BlockEntityStatesType));
        }

        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.Loading, BlockEntityStatesType));
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, BlockEntityStatesType));
        Assert.False(MessageRouter.IsAllowedFromServer(ClientState.AwaitingServerHello, BlockEntityStatesType));
        Assert.True(MessageRouter.IsGameMessageFromServer(BlockEntityStatesType));
    }

    [Fact]
    public void MessageRouter_BlockEntityStates_NeverAllowedFromClient()
    {
        // The control: the server may send it, so a refusal in the other direction is a rule and not an unknown type.
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, BlockEntityStatesType));

        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, BlockEntityStatesType));
        }

        Assert.False(MessageRouter.IsGameMessageFromClient(BlockEntityStatesType));
    }

    [Fact]
    public void MessageRouter_BlockEntityStates_TypeByteIsKnownAndReliable()
    {
        Assert.True(MessageRouter.TryGetType([46], out var type));
        Assert.Equal(BlockEntityStatesType, type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(BlockEntityStatesType));

        // Its neighbours in the survival range stay unassigned (47 is the world settings of K6-35 since K6-35, so the first unassigned neighbour is 50; 44 is PlayerVitals of K6-33, 45 PropStates of K6-31, 48 the player progress state of K6-32).
        foreach (byte neighbour in new byte[] { 50 })
        {
            Assert.False(MessageRouter.TryGetType([neighbour], out _));
        }
    }
}
