using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Space;

namespace Horyzont.Core.Tests.Serialization;

public sealed class StarSystemStateCodecTests
{
    [Fact]
    public void MessageCodec_StarSystemState_RoundTrip()
    {
        var message = new StarSystemState(StarSystemGenesis.CreateWorldBodies(0xDEADBEEF, "core:planet/1").System);
        var writer = new ByteWriter();
        MessageCodec.Write(writer, message);
        var frame = writer.WrittenSpan.ToArray();

        Assert.Equal(32, frame[0]);
        Assert.Equal(message, MessageCodec.Read(frame));
        Assert.Equal(StateCodec.EncodeStarSystemBlob(message.System)[2..], frame[2..]);
        Assert.InRange(frame.Length, 0, 300);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(MessageType.StarSystemState));
    }

    [Fact]
    public void MessageCodec_StarSystemState_EightBodiesFitAndDamagedRejected()
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, new StarSystemState(MaxMessages.MaxStarSystem()));
        var frame = writer.WrittenSpan.ToArray();

        Assert.InRange(frame.Length, 0, 560);
        frame[^1] = 0x7F;
        frame[^2] = 0xF0; // rotation W becomes infinite
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(frame));
    }
}
