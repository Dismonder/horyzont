using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

/// <summary>K5-25 X5: ShipInputBatch (30) from an InGame client, ShipCorrection (34) from the server.</summary>
public sealed class ShipMessageRouterTests
{
    [Fact]
    public void MessageRouter_ShipInputBatch_ClientInGameOnly()
    {
        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.Equal(state == PeerState.InGame, MessageRouter.IsAllowedFromClient(state, MessageType.ShipInputBatch));
        }

        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.False(MessageRouter.IsAllowedFromServer(state, MessageType.ShipInputBatch));
        }

        Assert.True(MessageRouter.IsGameMessageFromClient(MessageType.ShipInputBatch));
        Assert.False(MessageRouter.IsGameMessageFromServer(MessageType.ShipInputBatch));
        Assert.True(MessageRouter.TryGetType([30], out var type));
        Assert.Equal(MessageType.ShipInputBatch, type);
    }

    [Fact]
    public void MessageRouter_ShipCorrection_ServerInGameOnly()
    {
        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.False(MessageRouter.IsAllowedFromClient(state, MessageType.ShipCorrection));
        }

        Assert.True(MessageRouter.IsAllowedFromServer(ClientState.InGame, MessageType.ShipCorrection));
        Assert.True(MessageRouter.IsGameMessageFromServer(MessageType.ShipCorrection));
        Assert.False(MessageRouter.IsGameMessageFromClient(MessageType.ShipCorrection));
        Assert.True(MessageRouter.TryGetType([34], out var type));
        Assert.Equal(MessageType.ShipCorrection, type);
    }
}
