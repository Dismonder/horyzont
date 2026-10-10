using System.Numerics;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Serialization;

public sealed class TerrainSyncCodecTests
{
    private static readonly PlanetId Planet = new(1);
    private static byte[] Encode(Message message)
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, message);
        return writer.WrittenSpan.ToArray();
    }

    [Fact]
    public void Decode_TerrainSyncAndView_Roundtrips()
    {
        var entries = new[] { new TerrainSyncEntry(new TerrainBlockKey(Planet, 0, -2, 3, 4), 9, ulong.MaxValue) };
        var sync = new TerrainSync(1, 2, Planet, new OpId(7), 0, false, entries);
        var decoded = Assert.IsType<TerrainSync>(MessageCodec.Read(Encode(sync)));
        Assert.Equal(entries, decoded.Entries);
        Assert.Equal(sync with { Entries = decoded.Entries }, decoded);
        var view = new TerrainView(Planet, 3, new Vector3(1, 2, 3), 2048, new Vector3(4, 5, 6), 64);
        Assert.Equal(view, MessageCodec.Read(Encode(view)));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 0)]
    public void Encode_InvalidPurposeOrBarrier_Rejects(int purpose, int barrier)
    {
        Assert.Throws<ArgumentException>(() => Encode(new TerrainSync(1, (byte)purpose, Planet, new OpId((ulong)barrier), 0, true, [])));
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
        var bytes = Encode(new TerrainView(Planet, 1, Vector3.Zero, 2048, Vector3.Zero, 64));
        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes.AsSpan(0, length).ToArray();
            Assert.Throws<ProtocolViolationException>(() => MessageCodec.Read(truncated));
        }
    }

    [Fact]
    public void Encode_FinalPageWithEntriesOrInvalidView_Rejects()
    {
        var entry = new TerrainSyncEntry(new TerrainBlockKey(Planet, 0, 0, 0, 0), 1, 2);
        Assert.Throws<ArgumentException>(() => Encode(new TerrainSync(1, 0, Planet, default, 0, true, [entry])));
        Assert.Throws<ArgumentException>(() => Encode(new TerrainView(Planet, 1, Vector3.Zero, 16385, Vector3.Zero, 64)));
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
