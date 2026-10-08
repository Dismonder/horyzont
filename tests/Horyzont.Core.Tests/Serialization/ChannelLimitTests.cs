using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Message sizes against the channel limits, and the rule that an over-long frame is rejected before it is decoded.</summary>
public sealed class ChannelLimitTests
{
    public static TheoryData<MessageType> AllTypes()
    {
        var data = new TheoryData<MessageType>();
        foreach (var type in Enum.GetValues<MessageType>())
        {
            data.Add(type);
        }

        return data;
    }

    private static Message MaxOf(MessageType type) => MaxMessages.All().Single(m => m.Type == type);

    [Fact]
    public void MaxMessages_CoverEveryMessageType() =>
        Assert.Equal(Enum.GetValues<MessageType>().Order(), MaxMessages.All().Select(m => m.Type).Order());

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Write_LargestMessageOfEachType_FitsItsChannelLimit(MessageType type)
    {
        var message = MaxOf(type);
        var limit = MessageChannels.MaxFrameBytes(MessageChannels.For(type));

        var frame = Kit.Encode(message);

        Assert.True(frame.Length <= limit, $"{type}: {frame.Length} bytes exceed the {limit} byte limit");
        Assert.Equal((byte)type, frame[0]);
        Assert.Equal(1, frame[1]);
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Read_LargestMessageOfEachType_DecodesToTheSameMessage(MessageType type)
    {
        var message = MaxOf(type);

        var decoded = MessageCodec.Read(Kit.Encode(message));

        DeepAssert.Equal(Canon.Expected(message), decoded, Canon.HasCompressedRotation(message));
    }

    [Fact]
    public void Write_WorstSnapshot_IsTheComputedThousandAndThirtySevenBytes()
    {
        // 2 (type, version) + 5 + 10 + 5 + 5 (ids and tick) + 1 + 1 (counts) = 29,
        // 8 frames x 16 (kind 1 + id 10 + parent seq 5), 22 entities x 40 (id 10, fields 1, frame index 1, parent seq 5, position 12, rotation 4, velocity 6, flags 1).
        var frame = Kit.Encode(MaxMessages.Snapshot());

        Assert.Equal(29 + (8 * 16) + (22 * 40), frame.Length);
        Assert.Equal(1037, frame.Length);
        Assert.True(frame.Length <= 1150);
    }

    [Fact]
    public void Write_WorstConstructStatePart_IsAboutTwoHundredFortyOneKilobytes()
    {
        var frame = Kit.Encode(MaxMessages.All().Single(m => m.Type == MessageType.ConstructState));

        // 12 chunks x (15 position + 2 run count + 4096 runs x 5) plus the header: the plan quotes about 246 KB, inside the 256 KiB limit.
        Assert.InRange(frame.Length, 240_000, ProtocolConstants.MaxReliableMessageBytes);
    }

    [Fact]
    public void Write_LargestServerHello_FitsAndKeepsEveryRegistryEntry()
    {
        var hello = (ServerHello)MaxOf(MessageType.ServerHello);

        var decoded = Assert.IsType<ServerHello>(MessageCodec.Read(Kit.Encode(hello)));

        Assert.Equal(ProtocolConstants.MaxPlanetsInHello, decoded.Planets.Count);
        Assert.Equal(ProtocolConstants.MaxBlockTypes, decoded.BlockTypes.Count);
        Assert.Equal(ProtocolConstants.MaxMaterials, decoded.Materials.Count);
    }

    [Fact]
    public void Write_GeneratedMessages_AlwaysFitTheirChannel() =>
        Kit.ForAll(
            CodecGen.Message,
            message =>
            {
                var frame = Kit.Encode(message);
                var limit = MessageChannels.MaxFrameBytes(MessageChannels.For(message.Type));

                Assert.True(frame.Length <= limit, $"{message.Type}: {frame.Length} bytes exceed {limit}");
            });

    [Theory]
    [InlineData(MessageType.InputBatch)]
    [InlineData(MessageType.Snapshot)]
    [InlineData(MessageType.SnapshotAck)]
    [InlineData(MessageType.Ping)]
    [InlineData(MessageType.Pong)]
    public void Read_UnreliableFrameLongerThan1150Bytes_ThrowsProtocolViolation(MessageType type)
    {
        var frame = new byte[ProtocolConstants.MaxUnreliableMessageBytes + 1];
        frame[0] = (byte)type;
        frame[1] = 1;

        Kit.AssertViolation(d => MessageCodec.Read(d), frame);
    }

    [Theory]
    [InlineData(MessageType.ClientReady)]
    [InlineData(MessageType.TerrainBlockData)]
    [InlineData(MessageType.ConstructState)]
    [InlineData(MessageType.ServerHello)]
    public void Read_ReliableFrameLongerThan256KiB_ThrowsProtocolViolation(MessageType type)
    {
        var frame = new byte[ProtocolConstants.MaxReliableMessageBytes + 1];
        frame[0] = (byte)type;
        frame[1] = 1;

        Kit.AssertViolation(d => MessageCodec.Read(d), frame);
    }

    [Fact]
    public void Read_ReliableFrameOverTheUnreliableLimit_IsNotRejectedForItsSize()
    {
        // 2 + 1024 + ... bytes of ClientHello are far above 1150 and must still decode: the limit depends on the channel of the message type.
        var hello = (ClientHello)MaxOf(MessageType.ClientHello);

        var frame = Kit.Encode(hello with { AuthTicket = new byte[ProtocolConstants.MaxAuthTicketBytes] });

        Assert.True(frame.Length > ProtocolConstants.MaxUnreliableMessageBytes);
        Assert.IsType<ClientHello>(MessageCodec.Read(frame));
    }

    [Fact]
    public void Read_LimitIsCheckedBeforeDecoding_SoAnOversizedGarbageFrameCostsNothing()
    {
        var frame = new byte[ProtocolConstants.MaxReliableMessageBytes + 1];
        frame[0] = (byte)MessageType.TerrainBlockData;
        frame[1] = 1;
        Assert.IsType<ProtocolViolationException>(Record.Exception(() => MessageCodec.Read(frame)));

        var before = GC.GetAllocatedBytesForCurrentThread();
        Kit.AssertViolation(d => MessageCodec.Read(d), frame);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 16 * 1024, $"allocated {allocated} bytes");
    }
}
