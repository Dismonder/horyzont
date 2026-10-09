using System.Buffers.Binary;

using Horyzont.Core.Protocol;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Envelope of a saved terrain block: [kind][format version][voxel format][u32 length][voxels].</summary>
public sealed class TerrainBlockBlobTests
{
    private const int HeaderBytes = 7;
    private const int MaxVoxelBytes = ProtocolConstants.MaxTerrainBlockDataBytes - HeaderBytes;

    /// <summary>CodecGen.TerrainBlockBlob (Horyzont.Testing) repeats this header length as a literal; this pins it to the codec.</summary>
    [Fact]
    public void EncodeTerrainBlockBlob_EmptyPayload_HasTheHeaderLengthUsedByCodecGen() =>
        Assert.Equal(7, StateCodec.EncodeTerrainBlockBlob(1, []).Length);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(7, 3000)]
    [InlineData(255, MaxVoxelBytes)]
    public void EncodeDecode_RoundTrip(byte voxelFormat, int length)
    {
        var voxels = new byte[length];
        new Random(length).NextBytes(voxels);

        var blob = StateCodec.EncodeTerrainBlockBlob(voxelFormat, voxels);
        var decoded = StateCodec.DecodeTerrainBlockBlob(blob);

        Assert.Equal(length + HeaderBytes, blob.Length);
        Assert.Equal(voxelFormat, decoded.VoxelFormat);
        Assert.Equal(voxels, decoded.Voxels);
    }

    [Fact]
    public void Encode_GoldenBytes()
    {
        var blob = StateCodec.EncodeTerrainBlockBlob(1, [0xAA, 0xBB, 0xCC]);

        Assert.Equal(new byte[] { 5, 1, 1, 3, 0, 0, 0, 0xAA, 0xBB, 0xCC }, blob);
    }

    [Fact]
    public void Encode_VoxelFormatZero_Throws()
    {
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeTerrainBlockBlob(0, [1, 2, 3]));
    }

    [Fact]
    public void Encode_AboveLimit_Throws()
    {
        Assert.Throws<ArgumentException>(() => StateCodec.EncodeTerrainBlockBlob(1, new byte[MaxVoxelBytes + 1]));
    }

    public static TheoryData<string, byte[]> Malformed()
    {
        var valid = StateCodec.EncodeTerrainBlockBlob(1, [1, 2, 3, 4]);
        byte[] With(int index, byte value)
        {
            var copy = (byte[])valid.Clone();
            copy[index] = value;
            return copy;
        }

        byte[] WithLength(uint length)
        {
            var copy = (byte[])valid.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(3), length);
            return copy;
        }

        var oversized = new byte[ProtocolConstants.MaxTerrainBlockDataBytes + 1];
        oversized[0] = (byte)BlobKind.TerrainBlock;
        oversized[1] = 1;
        oversized[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(oversized.AsSpan(3), (uint)(oversized.Length - HeaderBytes));

        return new TheoryData<string, byte[]>
        {
            { "empty", [] },
            { "kind only", [(byte)BlobKind.TerrainBlock] },
            { "wrong kind", With(0, (byte)BlobKind.PlanetParams) },
            { "unknown version", With(1, 2) },
            { "voxel format 0", With(2, 0) },
            { "truncated header", valid[..5] },
            { "length too long", WithLength(5) },
            { "length too short", WithLength(3) },
            { "length huge", WithLength(uint.MaxValue) },
            { "trailing byte", [.. valid, 0] },
            { "truncated payload", valid[..^1] },
            { "over the size limit", oversized },
        };
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Decode_Malformed_ThrowsProtocolViolation(string name, byte[] blob)
    {
        Assert.NotNull(name);
        Assert.Throws<ProtocolViolationException>(() => StateCodec.DecodeTerrainBlockBlob(blob));
    }

    [Fact]
    public void Decode_ReturnsACopyIndependentOfTheInput()
    {
        var blob = StateCodec.EncodeTerrainBlockBlob(1, [9, 9]);
        var decoded = StateCodec.DecodeTerrainBlockBlob(blob);

        blob[^1] = 0;

        Assert.Equal(new byte[] { 9, 9 }, decoded.Voxels);
    }
}
