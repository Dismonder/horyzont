using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Message 48 (<see cref="PlayerProgressState"/>), format 1 (K6-32 D11): the bitset of the discoveries (varuint length 0 to 513, canonical bytes), a
/// varuint count of progress keys (0 to 256) and the keys in ascending <see cref="string.CompareOrdinal(string, string)"/> order (varuint length 1 to 64,
/// UTF-8). Reliable ordered, server to the owner. The enum member does not exist before step B2, so the type is the number 48. Every refusal test
/// first decodes or encodes the valid twin.
/// </summary>
public sealed class MessageCodecProgressTests
{
    private const MessageType ProgressType = (MessageType)48;

    private static ItemTypeBitSet Bits(params ushort[] types) => types.Aggregate(ItemTypeBitSet.Empty, (set, type) => set.With(new ItemTypeId(type)));

    private static string[] Keys(int count) => [.. Enumerable.Range(0, count).Select(i => $"k{i:D3}")];

    private static void AssertSame(PlayerProgressState expected, Message actual)
    {
        var state = Assert.IsType<PlayerProgressState>(actual);
        Assert.Equal(expected.Discovered, state.Discovered);
        Assert.Equal(expected.WorldProgressKeys, state.WorldProgressKeys);
    }

    public static TheoryData<PlayerProgressState> States() =>
    [
        new PlayerProgressState(ItemTypeBitSet.Empty, []),
        new PlayerProgressState(Bits(5), []),
        new PlayerProgressState(ItemTypeBitSet.Empty, ["test:boss_2"]),
        new PlayerProgressState(Bits(1, 5, 4096), ["test:boss_2", "test:boss_3"]),
        new PlayerProgressState(Bits(2), Keys(256)),
        new PlayerProgressState(ItemTypeBitSet.Empty, [new string('x', 64)]),
        new PlayerProgressState(ItemTypeBitSet.Empty, ["Zażółć"]),
    ];

    [Theory]
    [MemberData(nameof(States))]
    public void RoundTrip(PlayerProgressState message)
    {
        var frame = Kit.Encode(message);

        AssertSame(message, MessageCodec.Read(frame));
        Assert.Equal(48, frame[0]);
        Assert.Equal(1, frame[1]);
        Assert.Equal(ProgressType, message.Type);
        Assert.Equal(DeliveryChannel.ReliableOrdered, MessageChannels.For(ProgressType));
    }

    [Fact]
    public void GoldenBytes()
    {
        Assert.Equal("30 01 00 00", Kit.ToHex(Kit.Encode(new PlayerProgressState(ItemTypeBitSet.Empty, []))));
        Assert.Equal("30 01 01 20 00", Kit.ToHex(Kit.Encode(new PlayerProgressState(Bits(5), []))));
        Assert.Equal("30 01 01 20 01 06 74 3A 62 6F 73 73", Kit.ToHex(Kit.Encode(new PlayerProgressState(Bits(5), ["t:boss"]))));
        Assert.Equal("30 01 00 02 01 61 01 62", Kit.ToHex(Kit.Encode(new PlayerProgressState(ItemTypeBitSet.Empty, ["a", "b"]))));
    }

    [Fact]
    public void Write_MoreThan256Keys_Throws()
    {
        var full = new PlayerProgressState(ItemTypeBitSet.Empty, Keys(256));
        AssertSame(full, MessageCodec.Read(Kit.Encode(full)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(new PlayerProgressState(ItemTypeBitSet.Empty, Keys(257))));
    }

    [Fact]
    public void Write_UnsortedDuplicateOrInvalidKeys_Throws()
    {
        var valid = new PlayerProgressState(ItemTypeBitSet.Empty, ["a", "b"]);
        AssertSame(valid, MessageCodec.Read(Kit.Encode(valid)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { WorldProgressKeys = ["b", "a"] }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { WorldProgressKeys = ["a", "a"] }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { WorldProgressKeys = [string.Empty] }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { WorldProgressKeys = [new string('x', 65)] }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { WorldProgressKeys = ["a\ud800b"] }));
    }

    [Fact]
    public void KeyOrder_IsOrdinalOnUtf16_NotOnUtf8Bytes()
    {
        // U+1F600 is the surrogate pair D83D DE00 and sorts before U+E000 as UTF-16, but after it as UTF-8 (F0 9F 98 80 against EE 80 80).
        const string Emoji = "😀";
        const string PrivateUse = "";
        var ascending = new PlayerProgressState(ItemTypeBitSet.Empty, [Emoji, PrivateUse]);
        AssertSame(ascending, MessageCodec.Read(Kit.Encode(ascending)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(new PlayerProgressState(ItemTypeBitSet.Empty, [PrivateUse, Emoji])));
    }

    [Fact]
    public void Read_InvalidKeys_Throws()
    {
        AssertSame(new PlayerProgressState(ItemTypeBitSet.Empty, ["a", "b"]), MessageCodec.Read(Kit.Hex("30 01 00 02 01 61 01 62")));

        // 257 keys, unsorted, duplicate, empty, 65 bytes, invalid UTF-8, a missing key, trailing bytes.
        Kit.AssertMessageViolation("30 01 00 81 02");
        Kit.AssertMessageViolation("30 01 00 02 01 62 01 61");
        Kit.AssertMessageViolation("30 01 00 02 01 61 01 61");
        Kit.AssertMessageViolation("30 01 00 01 00");
        Kit.AssertMessageViolation("30 01 00 01 41 " + string.Join(' ', Enumerable.Repeat("78", 65)));
        Kit.AssertMessageViolation("30 01 00 01 02 C3 28");
        Kit.AssertMessageViolation("30 01 00 02 01 61");
        Kit.AssertMessageViolation("30 01 00 00 00");
    }

    [Fact]
    public void Read_InvalidBitset_Throws()
    {
        AssertSame(new PlayerProgressState(Bits(5), []), MessageCodec.Read(Kit.Hex("30 01 01 20 00")));
        var longest = Enumerable.Repeat((byte)2, 512).Append((byte)1).ToArray();
        AssertSame(
            new PlayerProgressState(ItemTypeBitSet.FromBytes(longest), []),
            MessageCodec.Read(Kit.Hex("30 01 81 04 " + string.Join(' ', Enumerable.Repeat("02", 512)) + " 01 00")));

        // A trailing zero byte, bit 0 (no item type), a bit above type 4096 in byte 512 (review B1, 1), 514 bytes, a bitset cut short.
        Kit.AssertMessageViolation("30 01 02 20 00 00");
        Kit.AssertMessageViolation("30 01 01 21 00");
        Kit.AssertMessageViolation("30 01 81 04 " + string.Join(' ', Enumerable.Repeat("00", 512)) + " 02 00");
        Kit.AssertMessageViolation("30 01 81 04 " + string.Join(' ', Enumerable.Repeat("02", 513)) + " 00");
        Kit.AssertMessageViolation("30 01 82 04 " + string.Join(' ', Enumerable.Repeat("02", 514)) + " 00");
        Kit.AssertMessageViolation("30 01 02 20");
    }

    [Fact]
    public void Read_FutureVersion_Throws()
    {
        AssertSame(new PlayerProgressState(ItemTypeBitSet.Empty, []), MessageCodec.Read(Kit.Hex("30 01 00 00")));

        // The version byte is 01 for every message (review B1, 2): messages are never stored, and the handshake demands an equal protocol version.
        // K6-33 extends 48 by appending fields at the end and raising the protocol version, not by a second version byte.
        Kit.AssertMessageViolation("30 02 00 00");
        Kit.AssertMessageViolation("30 00 00 00");
    }
}
