using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests.Session;

/// <summary>Routing rules of the container messages (K6-29W): 42 and 43 from a client in game, 40, 41 and 49 from the server.</summary>
public sealed class MessageRouterTests
{
    [Theory]
    [InlineData(MessageType.ContainerSubscribe)]
    [InlineData(MessageType.ContainerUnsubscribe)]
    public void ContainerRequests_AllowedFromClientInGameOnly(MessageType type)
    {
        Assert.True(MessageRouter.IsAllowedFromClient(PeerState.InGame, type));
        Assert.False(MessageRouter.IsAllowedFromClient(PeerState.Loading, type));
        Assert.False(MessageRouter.IsAllowedFromClient(PeerState.AwaitingHello, type));
        Assert.False(MessageRouter.IsAllowedFromClient(PeerState.Closing, type));
        Assert.True(MessageRouter.IsGameMessageFromClient(type));
        Assert.False(MessageRouter.IsGameMessageFromServer(type));
        Assert.False(MessageRouter.IsAllowedFromServer(ClientState.InGame, type));
    }

    [Theory]
    [InlineData(MessageType.ContainerState)]
    [InlineData(MessageType.ContainerDelta)]
    [InlineData(MessageType.ItemRegistry)]
    public void ContainerUpdates_AllowedFromServer(MessageType type)
    {
        Assert.True(MessageRouter.IsGameMessageFromServer(type));
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, type));
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.Loading, type));
        Assert.False(MessageRouter.IsAllowedFromServer(ClientState.AwaitingServerHello, type));
        Assert.False(MessageRouter.IsAllowedFromClient(PeerState.InGame, type));
        Assert.False(MessageRouter.IsGameMessageFromClient(type));
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(41, true)]
    [InlineData(42, true)]
    [InlineData(43, true)]
    [InlineData(49, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(39, false)]
    [InlineData(44, false)]
    [InlineData(48, true)]
    [InlineData(47, false)]
    [InlineData(50, false)]
    [InlineData(0, false)]
    public void TryGetType_KnowsTheSurvivalTypes(byte code, bool known)
    {
        Assert.Equal(known, MessageRouter.TryGetType([code, 1], out var type));
        if (known)
        {
            Assert.Equal(code, (byte)type);
        }
    }
}
