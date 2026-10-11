using Horyzont.Core.Protocol;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests;

public sealed class MessageRouterTests
{
    [Fact]
    public void TryGetType_UnassignedValues_ReturnsFalse()
    {
        Assert.False(MessageRouter.TryGetType([], out _));
        for (var value = 0; value <= 255; value++)
        {
            var known = MessageRouter.TryGetType([(byte)value], out var type);
            Assert.Equal(value is (>= 1 and <= 25) or 30 or 31 or 32 or 33 or 34, known);
            if (known)
            {
                Assert.Equal((MessageType)value, type);
            }
        }
    }

    [Fact]
    public void ResyncRequest_OnlyClientInGame_IsAllowed()
    {
        foreach (var state in Enum.GetValues<PeerState>())
        {
            Assert.Equal(state == PeerState.InGame, MessageRouter.IsAllowedFromClient(state, MessageType.ResyncRequest));
        }

        foreach (var state in Enum.GetValues<ClientState>())
        {
            Assert.False(MessageRouter.IsAllowedFromServer(state, MessageType.ResyncRequest));
        }

        Assert.True(MessageRouter.IsGameMessageFromClient(MessageType.ResyncRequest));
        Assert.False(MessageRouter.IsGameMessageFromServer(MessageType.ResyncRequest));
    }
}
