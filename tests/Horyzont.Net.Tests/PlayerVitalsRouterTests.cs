using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

/// <summary>
/// Routing of <c>PlayerVitals</c> (message 44, K6-33 D22): the server sends it to its owner in the state <see cref="ClientState.InGame"/> only; a client
/// never sends it. It is unreliable.
/// </summary>
public sealed class PlayerVitalsRouterTests
{
    private const MessageType VitalsType = MessageType.PlayerVitals;

    [Fact]
    public void PlayerVitals_InGame_ServerToClientAllowed()
    {
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, VitalsType));
        Assert.True(MessageRouter.IsGameMessageFromServer(VitalsType));
        Assert.True(MessageRouter.TryGetType([44], out var type));
        Assert.Equal(VitalsType, type);
        Assert.Equal(DeliveryChannel.Unreliable, MessageChannels.For(VitalsType));
    }

    [Fact]
    public void PlayerVitals_FromClient_Refused()
    {
        // The control: the server may send it, so a refusal in the other direction is a rule and not an unknown type.
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, VitalsType));

        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, VitalsType));
        }

        Assert.False(MessageRouter.IsGameMessageFromClient(VitalsType));
    }

    [Fact]
    public void PlayerVitals_BeforeInGame_Refused()
    {
        // The control: in game it is allowed. Before it (no connection, handshake, loading) the server has no player to send it to.
        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, VitalsType));

        foreach (var state in Enum.GetValues<ClientState>().Where(state => state != ClientState.InGame))
        {
            Assert.False(MessageRouter.IsAllowedFromServer(state, VitalsType), state.ToString());
        }
    }
}
