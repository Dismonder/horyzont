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
            Assert.Equal(value is >= 1 and <= 20, known);
            if (known)
            {
                Assert.Equal((MessageType)value, type);
            }
        }
    }
}
