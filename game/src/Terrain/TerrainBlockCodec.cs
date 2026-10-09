using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;

using Godot;

using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;

namespace Horyzont.Game.Terrain;

/// <summary>
/// Terrain block (16³ voxels, channels of <see cref="TerrainChannels"/>) to and from the blob saved in <c>terrain_blocks.data</c>
/// and sent as <c>TerrainBlockData</c>: the Core envelope (<see cref="StateCodec.EncodeTerrainBlockBlob"/>) around the Voxel Tools
/// block serializer. Thread-safe: <see cref="Decode"/> uses a scratch buffer per thread; dispose only after every caller stopped
/// (the voxel streams are closed).
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An injected instance (K2-09 contract); K3-16 reads old voxel formats through it.")]
public sealed class TerrainBlockCodec : IDisposable
{
    private const int ChannelCount = (int)VoxelBuffer.ChannelId.MaxChannels;

    // Voxel Tools CompressedData headers (streams/compressed_data.h, v1.7).
    private const byte HeaderNone = 0;
    private const byte HeaderZstd = 3;

    // Largest uncompressed v1.7 serialization of a 16³ block: version and sizes, every channel 64-bit and raw, trailing magic.
    private const int MaxRawPayloadBytes = 7 + (ChannelCount * (1 + (16 * 16 * 16 * 8))) + 4;

    private readonly ThreadLocal<VoxelBuffer> _scratch = new(static () => new VoxelBuffer(), trackAllValues: true);

    /// <summary>Encodes a block. The buffer is only read.</summary>
    /// <param name="block">A 16³ block.</param>
    /// <returns>A new blob; never changed afterwards.</returns>
    /// <exception cref="ArgumentException">The block has another size or the blob would exceed the protocol limit.</exception>
    /// <exception cref="InvalidOperationException">The serializer failed.</exception>
    public byte[] Encode(VoxelBuffer block)
    {
        ArgumentNullException.ThrowIfNull(block);
        CheckSize(block);
        var voxels = VoxelBlockSerializer.SerializeToByteArray(block, TerrainEditSemantics.BlobCompression);
        if (voxels.Length == 0)
        {
            throw new InvalidOperationException("The Voxel Tools block serializer returned no data.");
        }

        return StateCodec.EncodeTerrainBlockBlob(TerrainEditSemantics.VoxelFormat, voxels);
    }

    /// <summary>
    /// Decodes a blob into <paramref name="into"/> (resized and reformatted). <paramref name="into"/> is written only after the
    /// payload was fully validated; on an exception it is left as it was.
    /// </summary>
    /// <param name="blob">The blob.</param>
    /// <param name="into">Receives the block.</param>
    /// <exception cref="ProtocolViolationException">Bad envelope, unknown voxel format or a payload the serializer rejects.</exception>
    public void Decode(ReadOnlySpan<byte> blob, VoxelBuffer into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var envelope = StateCodec.DecodeTerrainBlockBlob(blob);
        if (envelope.VoxelFormat != TerrainEditSemantics.VoxelFormat)
        {
            throw new ProtocolViolationException($"Unknown terrain voxel format {envelope.VoxelFormat}.");
        }

        // The C# API reports no result: a rejected payload only prints an error, and Voxel Tools v1.7 resizes the buffer and sets the
        // channel depths before it finishes validating, so a failed decode can leave a plausible 16³ buffer. The payload is therefore
        // decoded into a scratch buffer and accepted only when serializing that buffer again (uncompressed) gives exactly the
        // decompressed payload (S2: serialize, deserialize, serialize is byte-identical); only then is it copied into the caller's
        // buffer. Comparing the uncompressed bytes keeps saved blocks readable if a later zstd emits different compressed bytes.
        var raw = Decompress(envelope.Voxels);
        var scratch = _scratch.Value!;
        scratch.Create(1, 1, 1);
        VoxelBlockSerializer.DeserializeFromByteArray(envelope.Voxels, scratch, true);
        if (!HasBlockLayout(scratch)
            || !VoxelBlockSerializer.SerializeToByteArray(scratch, VoxelBlockSerializer.Compression.None).AsSpan().SequenceEqual(raw))
        {
            throw new ProtocolViolationException("The terrain voxel payload was rejected by the block serializer.");
        }

        into.Create(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
        for (var channel = 0u; channel < ChannelCount; channel++)
        {
            into.SetChannelDepth(channel, scratch.GetChannelDepth(channel));
            into.CopyChannelFrom(scratch, channel);
        }
    }

    /// <summary>
    /// Digest of the voxel values of a block: XxHash64 of the raw SDF channel followed by the raw material channel. Uniform
    /// channels are expanded, so the digest does not depend on how Voxel Tools happens to store the block.
    /// </summary>
    /// <param name="block">A 16³ block.</param>
    /// <returns>The digest.</returns>
    public ulong Digest(VoxelBuffer block)
    {
        ArgumentNullException.ThrowIfNull(block);
        CheckSize(block);
        var hash = new XxHash64();
        hash.Append(block.GetChannelAsByteArray(TerrainChannels.Sdf));
        hash.Append(block.GetChannelAsByteArray(TerrainChannels.Material));
        return hash.GetCurrentHashAsUInt64();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var buffer in _scratch.Values)
        {
            buffer.Dispose();
        }

        _scratch.Dispose();
    }

    /// <summary>The uncompressed serialization inside a Voxel Tools CompressedData payload (no compression or zstd).</summary>
    private static ReadOnlySpan<byte> Decompress(byte[] payload)
    {
        if (payload.Length > 1 && payload[0] == HeaderNone)
        {
            return payload.AsSpan(1);
        }

        if (payload.Length > 5 && payload[0] == HeaderZstd)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(1));
            if (size is > 0 and <= MaxRawPayloadBytes)
            {
                var raw = GD.Decompress(payload[5..], size, Godot.FileAccess.CompressionMode.Zstd);
                if (raw.Length == size)
                {
                    return raw;
                }
            }
        }

        throw new ProtocolViolationException("The terrain voxel payload has an unsupported or broken compression header.");
    }

    private static bool HasBlockLayout(VoxelBuffer block)
    {
        var size = block.GetSize();
        return size.X == TerrainMath.BlockSize && size.Y == TerrainMath.BlockSize && size.Z == TerrainMath.BlockSize
            && block.GetChannelDepth((uint)TerrainChannels.Sdf) == TerrainChannels.SdfDepth
            && block.GetChannelDepth((uint)TerrainChannels.Material) == TerrainChannels.MaterialDepth;
    }

    private static void CheckSize(VoxelBuffer block)
    {
        var size = block.GetSize();
        if (size.X != TerrainMath.BlockSize || size.Y != TerrainMath.BlockSize || size.Z != TerrainMath.BlockSize)
        {
            throw new ArgumentException($"Terrain blocks are {TerrainMath.BlockSize}³ voxels, not {size}.", nameof(block));
        }
    }
}
