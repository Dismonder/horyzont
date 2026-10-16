using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

/// <summary>
/// Routing of <c>BlockEntityStates</c> (message 46, K6-30 D15): the server sends it in the states in which it sends
/// <see cref="MessageType.ConstructState"/>; a client never sends it. The enum member arrives with the protocol (step B2), so the test
/// names the type by number.
/// </summary>
public sealed class BlockEntityStatesRouterTests
{
    private const MessageType BlockEntityStatesType = (MessageType)46;

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

        // Its neighbours in the survival range stay unassigned (44, 45, 47, 48 belong to later packages).
        foreach (byte neighbour in new byte[] { 44, 45, 47, 48 })
        {
            Assert.False(MessageRouter.TryGetType([neighbour], out _));
        }
    }
}
