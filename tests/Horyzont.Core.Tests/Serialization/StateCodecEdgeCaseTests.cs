using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Malformed blobs, run-length encoding rules, registry entries and checkpoint records.</summary>
public sealed class StateCodecEdgeCaseTests
{
    private const string Zero12 = "00 00 00 00 00 00 00 00 00 00 00 00";
    private const string IdentityRotation = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F";

    private static readonly ConstructHeaderRecord _header =
        new(new EntityId(1), new FrameRef(FrameKind.System, 1), 2, ConstructMode.Ship, new Vector3(1, 2, 3), Quaternion.Identity, 4);

    private static readonly PlayerRecord _player = new(
        new PlayerId(1),
        new PlatformIdentity(IdentityPlatform.Steam, 76561198000000000UL),
        "Ann",
        new EntityId(2),
        new FrameRef(FrameKind.Planet, 3),
        4,
        new CellPos(1, 2, 3),
        new Vector3(1, 2, 3),
        Quaternion.Identity,
        0.25f,
        true);

    private static readonly PlanetParams _planet = new(new PlanetId(1), new SystemId(1), 7, "core:test/1");

    private static uint[] Chunk(uint fill = 0)
    {
        var blocks = new uint[4096];
        Array.Fill(blocks, fill);
        return blocks;
    }

    private static string Rle(params (int Length, uint Value)[] runs) =>
        VarUInt((uint)runs.Length) + " " + string.Join(
            ' ',
            runs.Select(r => VarUInt((uint)r.Length) + " " + string.Join(' ', BitConverter.GetBytes(r.Value).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)))));

    private static string VarUInt(uint value)
    {
        var parts = new List<string>();
        do
        {
            var low = (byte)(value & 0x7F);
            value >>= 7;
            parts.Add((value != 0 ? low | 0x80 : low).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        while (value != 0);

        return string.Join(' ', parts);
    }

    private static void AssertChunkBlobViolation(string rle) => Kit.AssertViolation(d => StateCodec.DecodeChunkBlob(d), Kit.Hex("03 01 " + rle));

    // ----- blobs: kind, version, trailing data -----------------------------------------------------------------------------------------------

    private static byte[][] AllBlobs() =>
    [
        StateCodec.EncodeConstructHeaderBlob(_header),
        StateCodec.EncodePlayerBlob(_player),
        StateCodec.EncodeChunkBlob(Chunk()),
        StateCodec.EncodePlanetBlob(_planet),
    ];

    private static readonly DecodeFn[] _blobDecoders =
    [
        d => StateCodec.DecodeConstructHeaderBlob(d),
        d => StateCodec.DecodePlayerBlob(d),
        d => StateCodec.DecodeChunkBlob(d),
        d => StateCodec.DecodePlanetBlob(d),
    ];

    [Fact]
    public void DecodeBlob_OfItsOwnKind_RoundTrips()
    {
        DeepAssert.Equal(_header, StateCodec.DecodeConstructHeaderBlob(StateCodec.EncodeConstructHeaderBlob(_header)));
        DeepAssert.Equal(_player, StateCodec.DecodePlayerBlob(StateCodec.EncodePlayerBlob(_player)));
        DeepAssert.Equal(_planet, StateCodec.DecodePlanetBlob(StateCodec.EncodePlanetBlob(_planet)));
        Assert.Equal(Chunk(), StateCodec.DecodeChunkBlob(StateCodec.EncodeChunkBlob(Chunk())));
    }

    [Fact]
    public void DecodeBlob_OfAnotherKind_ThrowsProtocolViolation()
    {
        var blobs = AllBlobs();

        for (var decoder = 0; decoder < _blobDecoders.Length; decoder++)
        {
            for (var blob = 0; blob < blobs.Length; blob++)
            {
                if (blob != decoder)
                {
                    Kit.AssertViolation(_blobDecoders[decoder], blobs[blob]);
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DecodeBlob_WithUnknownKindByte_ThrowsProtocolViolation(int which)
    {
        foreach (var kind in new byte[] { 0, 5, 0xFF })
        {
            var blob = AllBlobs()[which].ToArray();
            blob[0] = kind;
            Kit.AssertViolation(_blobDecoders[which], blob);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DecodeBlob_WithUnknownFormatVersion_ThrowsProtocolViolation(int which)
    {
        // The player blob (which = 1) has a version 2 since K6-33, so its first unknown version is 3.
        foreach (var version in which == 1 ? new byte[] { 0, 3, 0xFF } : new byte[] { 0, 2, 0xFF })
        {
            var blob = AllBlobs()[which].ToArray();
            blob[1] = version;
            Kit.AssertViolation(_blobDecoders[which], blob);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DecodeBlob_WithTrailingBytes_ThrowsProtocolViolation(int which)
    {
        var blob = Kit.Concat(AllBlobs()[which], [0x00]);

        Kit.AssertViolation(_blobDecoders[which], blob);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DecodeBlob_EmptyOrKindOnly_ThrowsProtocolViolation(int which)
    {
        var blob = AllBlobs()[which];

        Kit.AssertViolation(_blobDecoders[which], []);
        Kit.AssertViolation(_blobDecoders[which], blob[..1]);
        Kit.AssertViolation(_blobDecoders[which], blob[..2]);
    }

    // ----- run-length encoding -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ChunkBlob_SingleFullRunOfNonEmptyBlock_Decodes()
    {
        var block = BlockState.Create(9, 5, 2, 1).Packed;

        var blocks = StateCodec.DecodeChunkBlob(Kit.Hex("03 01 " + Rle((4096, block))));

        Assert.Equal(Chunk(block), blocks);
    }

    [Fact]
    public void ChunkBlob_TwoRunsOfDifferentValues_Decode()
    {
        var blocks = StateCodec.DecodeChunkBlob(Kit.Hex("03 01 " + Rle((2048, 0), (2048, 1))));

        Assert.Equal(0u, blocks[2047]);
        Assert.Equal(1u, blocks[2048]);
        Assert.Equal(4096, blocks.Length);
    }

    [Fact]
    public void ChunkBlob_RunLengthsNotSummingTo4096_ThrowProtocolViolation()
    {
        AssertChunkBlobViolation(Rle((4095, 0)));
        AssertChunkBlobViolation(Rle((4097, 0)));
        AssertChunkBlobViolation(Rle((4096, 0), (1, 1)));
        AssertChunkBlobViolation(Rle((2048, 0), (2047, 1)));
        AssertChunkBlobViolation(Rle((1, 0)));
    }

    [Fact]
    public void ChunkBlob_ZeroRunsOrZeroLengthRun_ThrowProtocolViolation()
    {
        AssertChunkBlobViolation("00");
        AssertChunkBlobViolation(Rle((0, 0), (4096, 1)));
        AssertChunkBlobViolation(Rle((4096, 1), (0, 0)));
    }

    [Fact]
    public void ChunkBlob_TwoAdjacentRunsWithTheSameValue_ThrowProtocolViolation()
    {
        AssertChunkBlobViolation(Rle((2048, 0), (2048, 0)));
        AssertChunkBlobViolation(Rle((1, 7), (2, 7), (4093, 0)));
        AssertChunkBlobViolation(Rle((4000, 0), (96, 0)));
    }

    [Fact]
    public void ChunkBlob_NonAdjacentRunsWithTheSameValue_AreFine()
    {
        var blocks = StateCodec.DecodeChunkBlob(Kit.Hex("03 01 " + Rle((1, 1), (4094, 0), (1, 1))));

        Assert.Equal(1u, blocks[0]);
        Assert.Equal(1u, blocks[4095]);
    }

    [Fact]
    public void ChunkBlob_MoreThan4096Runs_ThrowsProtocolViolation()
    {
        var runs = Enumerable.Range(0, 4097).Select(i => (1, (i & 1) == 0 ? 0u : 1u)).ToArray();

        AssertChunkBlobViolation(Rle(runs));
    }

    [Fact]
    public void ChunkBlob_FourThousandNinetySixRunsOfLengthOne_IsTheWorstCaseAndDecodes()
    {
        var runs = Enumerable.Range(0, 4096).Select(i => (1, (i & 1) == 0 ? 0u : 1u)).ToArray();

        var blocks = StateCodec.DecodeChunkBlob(Kit.Hex("03 01 " + Rle(runs)));

        Assert.Equal(Enumerable.Range(0, 4096).Select(i => (i & 1) == 0 ? 0u : 1u), blocks);
    }

    [Theory]
    [InlineData(0x10000001u)]
    [InlineData(0x80000001u)]
    [InlineData(0x00030000u)]
    [InlineData(0x00180001u)]
    [InlineData(0xFFFFFFFFu)]
    public void ChunkBlob_RunWithMalformedBlockState_ThrowsProtocolViolation(uint value) => AssertChunkBlobViolation(Rle((4096, value)));

    [Fact]
    public void ChunkBlob_RunLengthLargerThanUIntRange_ThrowsProtocolViolation() =>
        AssertChunkBlobViolation("01  FF FF FF FF 1F  00 00 00 00");

    [Fact]
    public void ChunkBlob_RedundantRunLengthEncoding_ThrowsProtocolViolation() => AssertChunkBlobViolation("01  80 A0 00  00 00 00 00");

    [Fact]
    public void ReadChunk_ReadsPositionThenBlocksAndUsesTheCallersConstruct()
    {
        var bytes = Kit.Hex("02 00 00  " + Rle((4096, 0)));

        var chunk = Kit.ReadAll(bytes, (ref ByteReader r) => StateCodec.ReadChunk(ref r, new EntityId(99)));

        Assert.Equal(new ConstructChunkKey(new EntityId(99), new ChunkPos(1, 0, 0)), chunk.Key);
        Assert.Equal(Chunk(), chunk.Blocks);
    }

    [Fact]
    public void WriteChunk_WritesPositionAsThreeVarIntsThenRle()
    {
        var chunk = new ConstructChunkRecord(new ConstructChunkKey(new EntityId(99), new ChunkPos(-1, 1, 0)), Chunk());

        Assert.Equal(Kit.Hex("01 02 00  " + Rle((4096, 0))), Kit.Write(w => StateCodec.WriteChunk(w, chunk)));
    }

    [Fact]
    public void ReadChunk_PositionVarIntBeyondIntRange_ThrowsProtocolViolation() =>
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadChunk(ref r, new EntityId(1))), Kit.Hex("80 80 80 80 10 00 00 " + Rle((4096, 0))));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(4097)]
    [InlineData(8192)]
    public void WriteChunkBlocks_WrongLength_ThrowsArgumentException(int length) =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => StateCodec.WriteChunkBlocks(w, new uint[length])));

    [Theory]
    [InlineData(0x10000001u)]
    [InlineData(0x00030000u)]
    [InlineData(0x00180001u)]
    public void WriteChunkBlocks_MalformedBlockState_ThrowsArgumentException(uint value)
    {
        var blocks = new uint[4096];
        blocks[100] = value;

        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => StateCodec.WriteChunkBlocks(w, blocks)));
    }

    [Fact]
    public void WriteChunkBlocks_UsesMaximalRunsSoRewritingKeepsTheBytes()
    {
        var blocks = Chunk();
        blocks[10] = 5;
        blocks[11] = 5;
        blocks[12] = 5;

        var bytes = Kit.Write(w => StateCodec.WriteChunkBlocks(w, blocks));
        var again = Kit.Write(w => StateCodec.WriteChunkBlocks(w, Kit.ReadAll(bytes, (ref ByteReader r) => StateCodec.ReadChunkBlocks(ref r))));

        Assert.Equal(Kit.Hex(Rle((10, 0), (3, 5), (4083, 0))), bytes);
        Assert.Equal(bytes, again);
    }

    // ----- registry entries ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void RegistryEntry_RoundTripsAtTheLimits()
    {
        var block = new RegistryEntry(2048, "core:stone");
        var material = new RegistryEntry(255, "stone");

        Assert.Equal(block, Kit.ReadAll(Kit.Write(w => StateCodec.WriteRegistryEntry(w, block, 2048)), (ref ByteReader r) => StateCodec.ReadRegistryEntry(ref r, 2048)));
        Assert.Equal(material, Kit.ReadAll(Kit.Write(w => StateCodec.WriteRegistryEntry(w, material, 255)), (ref ByteReader r) => StateCodec.ReadRegistryEntry(ref r, 255)));
    }

    [Theory]
    [InlineData("00 01 61", 2048)]
    [InlineData("81 10 01 61", 2048)]
    [InlineData("80 80 04 01 61", 2048)]
    [InlineData("80 02 01 61", 255)]
    [InlineData("00 01 61", 255)]
    public void ReadRegistryEntry_IdOutsideOneToMax_ThrowsProtocolViolation(string hex, int maxId) =>
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadRegistryEntry(ref r, maxId)), Kit.Hex(hex));

    [Fact]
    public void ReadRegistryEntry_KeyOfSixtyFourBytes_IsAcceptedAndSixtyFiveIsNot()
    {
        var ok = Kit.Hex("01 40 " + string.Join(' ', Enumerable.Repeat("61", 64)));
        Assert.Equal(64, Kit.ReadAll(ok, (ref ByteReader r) => StateCodec.ReadRegistryEntry(ref r, 255)).Key.Length);

        var bad = Kit.Hex("01 41 " + string.Join(' ', Enumerable.Repeat("61", 65)));
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadRegistryEntry(ref r, 255)), bad);
    }

    [Theory]
    [InlineData(0, 2048)]
    [InlineData(2049, 2048)]
    [InlineData(256, 255)]
    [InlineData(-1, 255)]
    public void WriteRegistryEntry_IdOutsideRange_ThrowsArgumentException(int id, int maxId) =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => StateCodec.WriteRegistryEntry(w, new RegistryEntry(unchecked((ushort)id), "k"), maxId)));

    [Fact]
    public void WriteRegistryEntry_KeyLongerThanSixtyFourBytes_ThrowsArgumentException() =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => StateCodec.WriteRegistryEntry(w, new RegistryEntry(1, new string('k', 65)), 255)));

    // ----- PlayerRecord / ConstructHeader / PlanetParams -------------------------------------------------------------------------------------------

    private static string PlayerHex(string platform = "02", string name = "01 61", string seat = "00", string online = "01") =>
        "07  " + platform + "  44 33 22 11 00 00 00 00  " + name + "  09  02 09  00  " + seat + "  " + Zero12 + "  " + IdentityRotation + "  00 00 00 00  " + online;

    [Fact]
    public void ReadPlayer_Control_Decodes()
    {
        var player = Kit.ReadAll(Kit.Hex(PlayerHex()), (ref ByteReader r) => StateCodec.ReadPlayer(ref r));

        Assert.Equal(new PlayerId(7), player.Player);
        Assert.Equal("a", player.DisplayName);
        Assert.Null(player.Seat);
        Assert.True(player.IsOnline);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("03")]
    public void ReadPlayer_UnknownPlatform_ThrowsProtocolViolation(string platform) =>
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadPlayer(ref r)), Kit.Hex(PlayerHex(platform: platform)));

    [Theory]
    [InlineData("02")]
    [InlineData("FF")]
    public void ReadPlayer_BadBooleanFlags_ThrowProtocolViolation(string flag)
    {
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadPlayer(ref r)), Kit.Hex(PlayerHex(seat: flag)));
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadPlayer(ref r)), Kit.Hex(PlayerHex(online: flag)));
    }

    [Fact]
    public void ReadPlayer_DisplayNameAtAndOverThirtyTwoBytes()
    {
        var at = Kit.ReadAll(Kit.Hex(PlayerHex(name: "20 " + string.Join(' ', Enumerable.Repeat("61", 32)))), (ref ByteReader r) => StateCodec.ReadPlayer(ref r));
        Assert.Equal(32, at.DisplayName.Length);

        Kit.AssertViolation(
            d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadPlayer(ref r)),
            Kit.Hex(PlayerHex(name: "21 " + string.Join(' ', Enumerable.Repeat("61", 33)))));
    }

    [Fact]
    public void ReadPlayer_SeatPresent_ReadsCell()
    {
        var player = Kit.ReadAll(Kit.Hex(PlayerHex(seat: "01 02 01 00")), (ref ByteReader r) => StateCodec.ReadPlayer(ref r));

        Assert.Equal(new CellPos(1, -1, 0), player.Seat);
    }

    [Fact]
    public void WritePlayer_DisplayNameOverThirtyTwoBytes_ThrowsArgumentException() =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => StateCodec.WritePlayer(w, _player with { DisplayName = new string('x', 33) })));

    [Fact]
    public void ReadConstructHeader_UnknownModeOrFrameKind_ThrowsProtocolViolation()
    {
        static string Header(string parent, string mode) => "05  " + parent + "  00  " + mode + "  " + Zero12 + "  " + IdentityRotation + "  01";

        Assert.Equal(
            ConstructMode.Ship,
            Kit.ReadAll(Kit.Hex(Header("02 09", "01")), (ref ByteReader r) => StateCodec.ReadConstructHeader(ref r)).Mode);
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadConstructHeader(ref r)), Kit.Hex(Header("02 09", "02")));
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadConstructHeader(ref r)), Kit.Hex(Header("00 09", "00")));
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadConstructHeader(ref r)), Kit.Hex(Header("04 09", "00")));
    }

    [Fact]
    public void ReadPlanet_GeneratorProfileAtAndOverSixtyFourBytes()
    {
        static string Planet(string profile) => "01 01 07 00 00 00 00 00 00 00 " + profile + " " + string.Join(' ', Enumerable.Repeat("00 00 80 3F", 6));

        var at = Kit.ReadAll(Kit.Hex(Planet("40 " + string.Join(' ', Enumerable.Repeat("61", 64)))), (ref ByteReader r) => StateCodec.ReadPlanet(ref r));
        Assert.Equal(64, at.GeneratorProfile.Length);

        Kit.AssertViolation(
            d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadPlanet(ref r)),
            Kit.Hex(Planet("41 " + string.Join(' ', Enumerable.Repeat("61", 65)))));
        Kit.AssertViolation(d => Kit.ReadAll(d, (ref ByteReader r) => StateCodec.ReadPlanet(ref r)), Kit.Hex(Planet("02 C3 28")));
    }

    [Fact]
    public void WritePlanet_GeneratorProfileOverSixtyFourBytes_ThrowsArgumentException() =>
        Assert.ThrowsAny<ArgumentException>(() => Kit.Write(w => StateCodec.WritePlanet(w, _planet with { GeneratorProfile = new string('x', 65) })));

    [Fact]
    public void PlanetParams_SeedIsFixedWidth_SoAFullUInt64SeedRoundTrips()
    {
        var planet = _planet with { Seed = ulong.MaxValue };

        var bytes = Kit.Write(w => StateCodec.WritePlanet(w, planet));

        // Id (1) + System (1) + Seed (8) + profile (1 + 11) + 6 floats (24).
        Assert.Equal(1 + 1 + 8 + 1 + "core:test/1".Length + 24, bytes.Length);
        DeepAssert.Equal(planet, Kit.ReadAll(bytes, (ref ByteReader r) => StateCodec.ReadPlanet(ref r)));
    }
}
