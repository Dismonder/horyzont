using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests;

public sealed class ResyncRequestCodecTests
{
    [Fact]
    public void Codec_ResyncRequestRoundTripAndMalformed_EnforcesCode21()
    {
        var writer = new ByteWriter();
        var request = new ResyncRequest(new EntityId(ulong.MaxValue));
        MessageCodec.Write(writer, request);
        Assert.Equal(21, writer.WrittenSpan[0]);
        Assert.Equal(1, writer.WrittenSpan[1]);
        Assert.Equal(request, MessageCodec.Read(writer.WrittenSpan));
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(request.Type));
        for (var length = 0; length < writer.Length; length++)
        {
            var truncated = writer.WrittenSpan[..length].ToArray();
            Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(truncated));
        }

        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read([21, 1, 0]));
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read([21, 2, 1]));
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read([21, 1, 1, 0]));
        Assert.Throws<ArgumentException>(() => MessageCodec.Write(new ByteWriter(), new ResyncRequest(default)));
    }

    [Fact]
    public void Codec_ResyncRequest_MatchesHandComputedBytes()
    {
        // Type 21 (0x15), format version 1, then the entity id 300 as a varuint: 300 = 0b1_0010_1100 -> 0xAC 0x02.
        byte[] golden = [0x15, 0x01, 0xAC, 0x02];
        var writer = new ByteWriter();
        MessageCodec.Write(writer, new ResyncRequest(new EntityId(300)));
        Assert.Equal(golden, writer.WrittenSpan.ToArray());
        Assert.Equal(new ResyncRequest(new EntityId(300)), MessageCodec.Read(golden));
    }
}
