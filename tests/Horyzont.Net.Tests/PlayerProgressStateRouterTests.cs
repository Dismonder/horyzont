using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

/// <summary>
/// Routing of <c>PlayerProgressState</c> (message 48, K6-32 D11): the server sends it in the states in which it sends
/// <see cref="MessageType.ContainerState"/> (after the container states of a joining player); a client never sends it. The enum member arrives with
/// step B2, so the type is the number 48.
/// </summary>
public sealed class PlayerProgressStateRouterTests
{
    private const MessageType ProgressType = (MessageType)48;

    [Fact]
    public void MessageRouter_PlayerProgressState_AllowedFromServerLikeContainerState()
    {
        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(MessageRouter.IsAllowedFromServer(state, MessageType.ContainerState), MessageRouter.IsAllowedFromServer(state, ProgressType));
        }

        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, ProgressType));
        Assert.False(MessageRouter.IsAllowedFromServer(ClientState.AwaitingServerHello, ProgressType));
        Assert.True(MessageRouter.IsGameMessageFromServer(ProgressType));
    }

    [Fact]
    public void MessageRouter_PlayerProgressState_NeverAllowedFromClient()
    {
        // The control: the server may send it, so a refusal in the other direction is a rule and not an unknown type.
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, ProgressType));

        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, ProgressType));
        }

        Assert.False(MessageRouter.IsGameMessageFromClient(ProgressType));
    }

    [Fact]
    public void MessageRouter_PlayerProgressState_TypeByteIsKnownAndReliable()
    {
        Assert.True(MessageRouter.TryGetType([48], out var type));
        Assert.Equal(ProgressType, type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(ProgressType));

        // Its neighbour 47 (world settings) stays unassigned: it belongs to a later package (44 is the vitals of K6-33).
        foreach (byte neighbour in new byte[] { 47 })
        {
            Assert.False(MessageRouter.TryGetType([neighbour], out _));
        }
    }
}
