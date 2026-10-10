using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests;

public sealed class ServerStatusCodecTests
{
    [Fact]
    public void Codec_StatusAndJoinUnavailable_RoundTripsAndRejectsOversize()
    {
        Message[] messages = [new ServerStatus(true, 2, new string('x', 256)), new JoinUnavailable(1, "mismatch"),
            new ServerStatus(false, 0, "Running"), new JoinUnavailable(255, "Unknown reason")];
        foreach (var message in messages)
        {
            var writer = new ByteWriter();
            MessageCodec.Write(writer, message);
            Assert.Equal(message, MessageCodec.Read(writer.WrittenSpan));
            Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(message.Type));
            Assert.Equal(1, writer.WrittenSpan[1]);
            for (int length = 0; length < writer.Length; length++)
            {
                byte[] truncated = writer.WrittenSpan[..length].ToArray();
                Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(truncated));
            }
        }
        Assert.Equal(22, (byte)MessageType.ServerStatus);
        Assert.Equal(23, (byte)MessageType.JoinUnavailable);
        Assert.Throws<ArgumentException>(() => MessageCodec.Write(new ByteWriter(), new ServerStatus(true, 2, new string('x', 257))));
        Assert.Throws<ArgumentException>(() => MessageCodec.Write(new ByteWriter(), new JoinUnavailable(1, new string('x', 257))));
    }

    [Fact]
    public void Codec_InvalidBoolAndUnknownUnassignedType_Rejects()
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, new ServerStatus(true, 2, "fault"));
        byte[] frame = writer.WrittenSpan.ToArray();
        frame[2] = 2;
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(frame));
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(new byte[] {21, 1}));
    }
}
