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
/// block serializer. Stateless and thread-safe.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An injected instance (K2-09 contract); K3-16 reads old voxel formats through it.")]
public sealed class TerrainBlockCodec
{
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

    /// <summary>Decodes a blob into <paramref name="into"/> (resized and reformatted by the decoder).</summary>
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

        // The serializer reports a rejected payload only by printing an error and leaving the buffer as it was, so the buffer is
        // shrunk first and checked afterwards.
        into.Create(1, 1, 1);
        VoxelBlockSerializer.DeserializeFromByteArray(envelope.Voxels, into, true);
        var size = into.GetSize();
        if (size.X != TerrainMath.BlockSize || size.Y != TerrainMath.BlockSize || size.Z != TerrainMath.BlockSize
            || into.GetChannelDepth((uint)TerrainChannels.Sdf) != TerrainChannels.SdfDepth
            || into.GetChannelDepth((uint)TerrainChannels.Material) != TerrainChannels.MaterialDepth)
        {
            throw new ProtocolViolationException("The terrain voxel payload was rejected by the block serializer.");
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

    private static void CheckSize(VoxelBuffer block)
    {
        var size = block.GetSize();
        if (size.X != TerrainMath.BlockSize || size.Y != TerrainMath.BlockSize || size.Z != TerrainMath.BlockSize)
        {
            throw new ArgumentException($"Terrain blocks are {TerrainMath.BlockSize}³ voxels, not {size}.", nameof(block));
        }
    }
}
