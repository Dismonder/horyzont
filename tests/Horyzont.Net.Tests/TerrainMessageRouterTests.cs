using Horyzont.Core.Protocol;
using Horyzont.Net.Session;
namespace Horyzont.Net.Tests;
public sealed class TerrainMessageRouterTests
{
    [Fact]
    public void Route_TerrainTypes_OnlyAllowedDirectionsAndStates()
    {
        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.Equal(state is PeerState.Loading or PeerState.InGame, MessageRouter.IsAllowedFromClient(state, MessageType.TerrainView));
            Assert.False(MessageRouter.IsAllowedFromClient(state, MessageType.TerrainSync));
        }
        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.Equal(state is ClientState.Loading or ClientState.InGame, MessageRouter.IsAllowedFromServer(state, MessageType.TerrainSync));
            Assert.False(MessageRouter.IsAllowedFromServer(state, MessageType.TerrainView));
        }
        Assert.True(MessageRouter.IsGameMessageFromClient(MessageType.TerrainView));
        Assert.True(MessageRouter.IsGameMessageFromServer(MessageType.TerrainSync));
    }
}
