using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Serialization;

public sealed class TerrainSyncCodecTests
{
    private static readonly PlanetId _planet = new(1);
    private static byte[] Encode(Message message)
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, message);
        return writer.WrittenSpan.ToArray();
    }

    [Fact]
    public void Decode_TerrainSyncAndView_Roundtrips()
    {
        var entries = new[] { new TerrainSyncEntry(new TerrainBlockKey(_planet, 0, -2, 3, 4), 9, ulong.MaxValue) };
        var sync = new TerrainSync(1, 2, _planet, new OpId(7), 0, false, entries);
        var decoded = Assert.IsType<TerrainSync>(MessageCodec.Read(Encode(sync)));
        Assert.Equal(entries, decoded.Entries);
        Assert.Equal(sync with { Entries = decoded.Entries }, decoded);
        var view = new TerrainView(_planet, 3, new Vector3(1, 2, 3), 2048, new Vector3(4, 5, 6), 64);
        Assert.Equal(view, MessageCodec.Read(Encode(view)));
    }

    private static readonly byte[] _goldenColdCache =
    [
        0x18, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x01, 0x00, 0x03, 0x06, 0x08, 0x09,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    private static readonly byte[] _goldenInterest =
    [
        0x18, 0x01, 0x82, 0x01, 0x01, 0x01, 0x00, 0x02, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x00, 0x00, 0xAC, 0x02,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
    ];

    private static readonly byte[] _goldenBarrier = [0x18, 0x01, 0x05, 0x02, 0x01, 0xAC, 0x02, 0x00, 0x01, 0x00];

    private static readonly byte[] _goldenView =
    [
        0x19, 0x01, 0x01, 0x03,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x40, 0x40,
        0x00, 0x00, 0x00, 0x45,
        0x00, 0x00, 0x80, 0x40, 0x00, 0x00, 0xA0, 0x40, 0x00, 0x00, 0xC0, 0x40,
        0x00, 0x00, 0x80, 0x42,
    ];

    private static TerrainSync ColdCache() => new(1, 0, _planet, default, 0, false,
        [new TerrainSyncEntry(new TerrainBlockKey(_planet, 0, -2, 3, 4), 9, ulong.MaxValue)]);

    private static TerrainSync Interest() => new(130, 1, _planet, default, 2, false,
        [new TerrainSyncEntry(new TerrainBlockKey(_planet, 0, 0, 0, 0), 300, 0x0102030405060708UL)]);

    private static TerrainSync Barrier() => new(5, 2, _planet, new OpId(300), 0, true, []);

    private static TerrainView View() => new(_planet, 3, new Vector3(1, 2, 3), 2048, new Vector3(4, 5, 6), 64);

    [Fact]
    public void Encode_TerrainSyncAndView_MatchesGoldenBytes()
    {
        Assert.Equal(_goldenColdCache, Encode(ColdCache()));
        Assert.Equal(_goldenInterest, Encode(Interest()));
        Assert.Equal(_goldenBarrier, Encode(Barrier()));
        Assert.Equal(_goldenView, Encode(View()));
    }

    [Fact]
    public void Decode_GoldenBytes_ProducesExpectedMessages()
    {
        AssertSync(ColdCache(), MessageCodec.Read(_goldenColdCache));
        AssertSync(Interest(), MessageCodec.Read(_goldenInterest));
        AssertSync(Barrier(), MessageCodec.Read(_goldenBarrier));
        Assert.Equal(View(), MessageCodec.Read(_goldenView));
    }

    private static void AssertSync(TerrainSync expected, Message actual)
    {
        var decoded = Assert.IsType<TerrainSync>(actual);
        Assert.Equal(expected.Entries, decoded.Entries);
        Assert.Equal(expected with { Entries = decoded.Entries }, decoded);
    }

    private static byte[] Patched(byte[] golden, int index, params byte[] bytes)
    {
        var copy = (byte[])golden.Clone();
        bytes.CopyTo(copy, index);
        return copy;
    }

    public static TheoryData<string, byte[]> InvalidFrames() => new()
    {
        { "purpose 3", Patched(_goldenColdCache, 3, 0x03) },
        { "zero transfer", Patched(_goldenColdCache, 2, 0x00) },
        { "zero planet", Patched(_goldenColdCache, 4, 0x00) },
        { "barrier with purpose 0", Patched(_goldenColdCache, 5, 0x07) },
        { "purpose 2 without barrier", Patched(_goldenBarrier, 5, 0x00, 0x00) },
        { "last page with entries", Patched(_goldenColdCache, 7, 0x01) },
        { "wrong planet key", Patched(_goldenColdCache, 9, 0x02) },
        { "lod above zero key", Patched(_goldenColdCache, 10, 0x01) },
        { "view NaN visual distance", Patched(_goldenView, 16, 0x00, 0x00, 0xC0, 0x7F) },
        { "view NaN collision distance", Patched(_goldenView, 32, 0x00, 0x00, 0xC0, 0x7F) },
        { "view infinite position", Patched(_goldenView, 4, 0x00, 0x00, 0x80, 0x7F) },
        { "view visual distance above 16384", Patched(_goldenView, 16, 0x00, 0x20, 0x80, 0x46) },
        { "view collision distance above 16384", Patched(_goldenView, 32, 0x00, 0x20, 0x80, 0x46) },
        { "view negative distance", Patched(_goldenView, 16, 0x00, 0x00, 0x80, 0xBF) },
        { "view zero planet", Patched(_goldenView, 2, 0x00) },
        { "view zero sequence", Patched(_goldenView, 3, 0x00) },
    };

    [Theory]
    [MemberData(nameof(InvalidFrames))]
    public void Decode_TerrainSyncAndView_InvalidLimitsFailBeforeAllocation(string name, byte[] frame)
    {
        Assert.NotEmpty(name);
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(frame));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 0)]
    public void Encode_InvalidPurposeOrBarrier_Rejects(int purpose, int barrier)
    {
        Assert.Throws<ArgumentException>(() => Encode(new TerrainSync(1, (byte)purpose, _planet, new OpId((ulong)barrier), 0, true, [])));
    }

    [Fact]
    public void Decode_DuplicateOrUnsortedKeys_Rejects()
    {
        var w = Header();
        w.WriteVarUInt(2);
        for (var i = 0; i < 2; i++)
        {
            w.WriteVarUInt(1); w.WriteByte(0);
            w.WriteVarInt(0); w.WriteVarInt(0); w.WriteVarInt(0);
            w.WriteVarUInt(1); w.WriteUInt64(2);
        }
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(w.WrittenSpan));
    }

    [Fact]
    public void Decode_ExcessiveCount_FailsBeforeEntryAllocation()
    {
        var w = Header();
        w.WriteVarUInt(1025);
        Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(w.WrittenSpan));
    }

    [Fact]
    public void Decode_EveryTruncation_Rejects()
    {
        var bytes = Encode(new TerrainView(_planet, 1, Vector3.Zero, 2048, Vector3.Zero, 64));
        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes.AsSpan(0, length).ToArray();
            Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(truncated));
        }
    }

    [Fact]
    public void Encode_FinalPageWithEntriesOrInvalidView_Rejects()
    {
        var entry = new TerrainSyncEntry(new TerrainBlockKey(_planet, 0, 0, 0, 0), 1, 2);
        Assert.Throws<ArgumentException>(() => Encode(new TerrainSync(1, 0, _planet, default, 0, true, [entry])));
        Assert.Throws<ArgumentException>(() => Encode(new TerrainView(_planet, 1, Vector3.Zero, 16385, Vector3.Zero, 64)));
        Assert.Throws<ArgumentException>(() => Encode(new TerrainView(default, 1, Vector3.Zero, 2048, Vector3.Zero, 64)));
    }

    private static ByteWriter Header()
    {
        var w = new ByteWriter();
        w.WriteByte(24); w.WriteByte(1);
        w.WriteVarUInt(1); w.WriteByte(0); w.WriteVarUInt(1); w.WriteVarUInt(0);
        w.WriteVarUInt(0); w.WriteBool(false);
        return w;
    }
}
