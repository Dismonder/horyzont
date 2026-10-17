using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Message 48 (<see cref="PlayerProgressState"/>), format 1 (K6-32 D11, K6-33 D6): the bitset of the discoveries (varuint length 0 to 513, canonical bytes),
/// a varuint count of progress keys (0 to 256), the keys in ascending <see cref="string.CompareOrdinal(string, string)"/> order (varuint length 1 to 64,
/// UTF-8) and, appended by K6-33, three food slots (each a varuint type 0 to 4096 and, with a type other than 0, a varuint tick of eating). The version
/// byte stays 01 and the protocol version rises, so a frame without the tail is an error. Reliable ordered, server to the owner. Every refusal test
/// first decodes or encodes the valid twin.
/// </summary>
public sealed class MessageCodecProgressTests
{
    private const MessageType ProgressType = (MessageType)48;

    /// <summary>The tail of three empty food slots.</summary>
    private const string NoFood = " 00 00 00" + Respawn;

    /// <summary>The tail of K6-35 for a player with no bed, no transition and no tombstones: twelve bytes (the layout is in <c>DeathCodecTests</c>).</summary>
    private const string Respawn = " 00 00 00 00 00 00 00 00 01 00 00 00";

    private static ItemTypeBitSet Bits(params ushort[] types) => types.Aggregate(ItemTypeBitSet.Empty, (set, type) => set.With(new ItemTypeId(type)));

    private static string[] Keys(int count) => [.. Enumerable.Range(0, count).Select(i => $"k{i:D3}")];

    private static FoodSlot Slot(ushort type, ulong eaten) => new(new ItemTypeId(type), new Tick(eaten));

    private static void AssertSame(PlayerProgressState expected, Message actual)
    {
        var state = Assert.IsType<PlayerProgressState>(actual);
        Assert.Equal(expected.Discovered, state.Discovered);
        Assert.Equal(expected.WorldProgressKeys, state.WorldProgressKeys);
        Assert.Equal(expected.Food, state.Food);
    }

    public static TheoryData<PlayerProgressState> States() =>
    [
        TestMessages.Progress(ItemTypeBitSet.Empty, [], FoodSlots.Empty),
        TestMessages.Progress(Bits(5), [], FoodSlots.Empty),
        TestMessages.Progress(ItemTypeBitSet.Empty, ["test:boss_2"], FoodSlots.Empty),
        TestMessages.Progress(Bits(1, 5, 4096), ["test:boss_2", "test:boss_3"], FoodSlots.Empty),
        TestMessages.Progress(Bits(2), Keys(256), FoodSlots.Empty),
        TestMessages.Progress(ItemTypeBitSet.Empty, [new string('x', 64)], FoodSlots.Empty),
        TestMessages.Progress(ItemTypeBitSet.Empty, ["Zażółć"], FoodSlots.Empty),
        TestMessages.Progress(ItemTypeBitSet.Empty, [], new FoodSlots(Slot(5, 100), default, default)),
        TestMessages.Progress(Bits(5, 6), ["test:boss_2"], new FoodSlots(Slot(5, 100), Slot(6, 200), Slot(7, 0))),
        TestMessages.Progress(ItemTypeBitSet.Empty, [], new FoodSlots(default, Slot(4096, ulong.MaxValue), default)),
        TestMessages.Progress(Bits(2), Keys(256), new FoodSlots(Slot(1, 1), Slot(2, 2), Slot(3, 3))),
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
        // The bytes of K6-32 with the tail of three empty slots (00 00 00): only the tail is new, the rest is unchanged (K6-33 D6).
        Assert.Equal("30 01 00 00 00 00 00" + Respawn, Kit.ToHex(Kit.Encode(TestMessages.Progress(ItemTypeBitSet.Empty, [], FoodSlots.Empty))));
        Assert.Equal("30 01 01 20 00 00 00 00" + Respawn, Kit.ToHex(Kit.Encode(TestMessages.Progress(Bits(5), [], FoodSlots.Empty))));
        Assert.Equal("30 01 01 20 01 06 74 3A 62 6F 73 73 00 00 00" + Respawn, Kit.ToHex(Kit.Encode(TestMessages.Progress(Bits(5), ["t:boss"], FoodSlots.Empty))));
        Assert.Equal("30 01 00 02 01 61 01 62 00 00 00" + Respawn, Kit.ToHex(Kit.Encode(TestMessages.Progress(ItemTypeBitSet.Empty, ["a", "b"], FoodSlots.Empty))));
    }

    [Fact]
    public void Read_DuplicateFoodTypes_Accepted()
    {
        // Approved at the B1 review: the codec checks the shape only; repeated types are refused by the builder of blob 22, and the client applies the
        // state of the server as it is.
        var distinct = Assert.IsType<PlayerProgressState>(MessageCodec.Read(Kit.Hex("30 01 00 00 05 64 06 C8 01 00" + Respawn)));
        Assert.Equal(new FoodSlots(Slot(5, 100), Slot(6, 200), default), distinct.Food);

        var repeated = Assert.IsType<PlayerProgressState>(MessageCodec.Read(Kit.Hex("30 01 00 00 05 64 05 C8 01 00" + Respawn)));
        Assert.Equal(new FoodSlots(Slot(5, 100), Slot(5, 200), default), repeated.Food);
    }

    [Fact]
    public void ProgressState_WithFood_GoldenBytes()
    {
        // One slot (type 5 eaten in tick 100), then three slots (types 5, 6, 7; ticks 100, 200 as C8 01, 0), then the largest type with a two-byte tick.
        Assert.Equal(
            "30 01 01 20 00 05 64 00 00" + Respawn,
            Kit.ToHex(Kit.Encode(TestMessages.Progress(Bits(5), [], new FoodSlots(Slot(5, 100), default, default)))));
        Assert.Equal(
            "30 01 01 20 00 05 64 06 C8 01 07 00" + Respawn,
            Kit.ToHex(Kit.Encode(TestMessages.Progress(Bits(5), [], new FoodSlots(Slot(5, 100), Slot(6, 200), Slot(7, 0))))));
        Assert.Equal(
            "30 01 00 00 00 80 20 80 01 00" + Respawn,
            Kit.ToHex(Kit.Encode(TestMessages.Progress(ItemTypeBitSet.Empty, [], new FoodSlots(default, Slot(4096, 128), default)))));
    }

    [Fact]
    public void ProgressState_WithoutFoodTail_Throws()
    {
        // The tail is mandatory: the version byte stays 01 and the handshake demands an equal protocol version (K6-33 D6).
        AssertSame(TestMessages.Progress(ItemTypeBitSet.Empty, [], FoodSlots.Empty), MessageCodec.Read(Kit.Hex("30 01 00 00 00 00 00" + Respawn)));

        // The tail of K6-35 is mandatory as well: the K6-33 frame ends where the new tail should begin.
        Kit.AssertMessageViolation("30 01 00 00 00 00 00");

        // The K6-32 frame without any tail, one and two empty slots only, and a filled slot cut before its tick.
        Kit.AssertMessageViolation("30 01 00 00");
        Kit.AssertMessageViolation("30 01 00 00 00");
        Kit.AssertMessageViolation("30 01 00 00 00 00");
        Kit.AssertMessageViolation("30 01 01 20 00 05");
        Kit.AssertMessageViolation("30 01 01 20 00 05 64 00");
    }

    [Fact]
    public void ProgressState_FoodTypeAbove4096_Throws()
    {
        // 4096 (80 20) is the largest type, 4097 (81 20) is not, in any of the three slots.
        AssertSame(
            TestMessages.Progress(ItemTypeBitSet.Empty, [], new FoodSlots(Slot(4096, 0), default, default)),
            MessageCodec.Read(Kit.Hex("30 01 00 00 80 20 00 00 00" + Respawn)));

        Kit.AssertMessageViolation("30 01 00 00 81 20 00 00 00" + Respawn);
        Kit.AssertMessageViolation("30 01 00 00 00 81 20 00 00" + Respawn);
        Kit.AssertMessageViolation("30 01 00 00 00 00 81 20 00" + Respawn);
        Kit.AssertMessageViolation("30 01 00 00 80 80 04 00 00 00" + Respawn);
    }

    [Fact]
    public void Write_InvalidFood_Throws()
    {
        var valid = TestMessages.Progress(ItemTypeBitSet.Empty, [], new FoodSlots(Slot(5, 100), default, default));
        AssertSame(valid, MessageCodec.Read(Kit.Encode(valid)));

        // A type above 4096 and an empty slot with a tick (the codec writes the tick only for a filled slot, so the state would not round-trip).
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Food = new FoodSlots(Slot(4097, 1), default, default) }));
        Assert.Throws<ArgumentException>(() => Kit.Encode(valid with { Food = new FoodSlots(default, default, Slot(0, 7)) }));
    }

    [Fact]
    public void Write_MoreThan256Keys_Throws()
    {
        var full = TestMessages.Progress(ItemTypeBitSet.Empty, Keys(256), FoodSlots.Empty);
        AssertSame(full, MessageCodec.Read(Kit.Encode(full)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(TestMessages.Progress(ItemTypeBitSet.Empty, Keys(257), FoodSlots.Empty)));
    }

    [Fact]
    public void Write_UnsortedDuplicateOrInvalidKeys_Throws()
    {
        var valid = TestMessages.Progress(ItemTypeBitSet.Empty, ["a", "b"], FoodSlots.Empty);
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
        var ascending = TestMessages.Progress(ItemTypeBitSet.Empty, [Emoji, PrivateUse], FoodSlots.Empty);
        AssertSame(ascending, MessageCodec.Read(Kit.Encode(ascending)));

        Assert.Throws<ArgumentException>(() => Kit.Encode(TestMessages.Progress(ItemTypeBitSet.Empty, [PrivateUse, Emoji], FoodSlots.Empty)));
    }

    [Fact]
    public void Read_InvalidKeys_Throws()
    {
        AssertSame(TestMessages.Progress(ItemTypeBitSet.Empty, ["a", "b"], FoodSlots.Empty), MessageCodec.Read(Kit.Hex("30 01 00 02 01 61 01 62" + NoFood)));

        // 257 keys (the count alone fails), unsorted, duplicate, empty, 65 bytes, invalid UTF-8, a missing key and the tail one byte too long. With the tail
        // in place the key is the only difference from the valid twin; the K6-32 frame "30 01 00 00 00" now means "food cut short" (see the test of the tail).
        Kit.AssertMessageViolation("30 01 00 81 02");
        Kit.AssertMessageViolation("30 01 00 02 01 62 01 61" + NoFood);
        Kit.AssertMessageViolation("30 01 00 02 01 61 01 61" + NoFood);
        Kit.AssertMessageViolation("30 01 00 01 00" + NoFood);
        Kit.AssertMessageViolation("30 01 00 01 41 " + string.Join(' ', Enumerable.Repeat("78", 65)) + NoFood);
        Kit.AssertMessageViolation("30 01 00 01 02 C3 28" + NoFood);
        Kit.AssertMessageViolation("30 01 00 02 01 61");
        Kit.AssertMessageViolation("30 01 00 00 00 00 00" + Respawn + " 00");
    }

    [Fact]
    public void Read_InvalidBitset_Throws()
    {
        AssertSame(TestMessages.Progress(Bits(5), [], FoodSlots.Empty), MessageCodec.Read(Kit.Hex("30 01 01 20 00" + NoFood)));
        var longest = Enumerable.Repeat((byte)2, 512).Append((byte)1).ToArray();
        AssertSame(
            TestMessages.Progress(ItemTypeBitSet.FromBytes(longest), [], FoodSlots.Empty),
            MessageCodec.Read(Kit.Hex("30 01 81 04 " + string.Join(' ', Enumerable.Repeat("02", 512)) + " 01 00" + NoFood)));

        // A trailing zero byte, bit 0 (no item type), a bit above type 4096 in byte 512 (review B1, 1), 514 bytes, a bitset cut short.
        Kit.AssertMessageViolation("30 01 02 20 00 00" + NoFood);
        Kit.AssertMessageViolation("30 01 01 21 00" + NoFood);
        Kit.AssertMessageViolation("30 01 81 04 " + string.Join(' ', Enumerable.Repeat("00", 512)) + " 02 00" + NoFood);
        Kit.AssertMessageViolation("30 01 81 04 " + string.Join(' ', Enumerable.Repeat("02", 513)) + " 00" + NoFood);
        Kit.AssertMessageViolation("30 01 82 04 " + string.Join(' ', Enumerable.Repeat("02", 514)) + " 00" + NoFood);
        Kit.AssertMessageViolation("30 01 02 20");
    }

    [Fact]
    public void Read_FutureVersion_Throws()
    {
        AssertSame(TestMessages.Progress(ItemTypeBitSet.Empty, [], FoodSlots.Empty), MessageCodec.Read(Kit.Hex("30 01 00 00" + NoFood)));

        // The version byte is 01 for every message (review B1, 2): messages are never stored, and the handshake demands an equal protocol version.
        // K6-33 extended 48 by appending the food at the end and raising the protocol version, not by a second version byte.
        Kit.AssertMessageViolation("30 02 00 00" + NoFood);
        Kit.AssertMessageViolation("30 00 00 00" + NoFood);
    }
}
