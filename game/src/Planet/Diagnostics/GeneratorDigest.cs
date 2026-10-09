using System.Buffers.Binary;
using System.IO.Hashing;

using Godot;

namespace Horyzont.Game.Planet.Diagnostics;

/// <summary>Digest of one generated block: XxHash64 of the raw bytes of the SDF channel and of the material channel.</summary>
public readonly record struct BlockDigest(BlockSample Block, ulong Sdf, ulong Material, bool SdfUniform);

/// <summary>Generates blocks and digests their channels. Not thread safe.</summary>
public sealed class GeneratorDigest : IDisposable
{
    private readonly VoxelGenerator _generator;
    private readonly VoxelBuffer _buffer = new();
    private readonly VoxelFormat _format = TerrainChannels.CreateFormat();

    /// <summary>Creates a digester for <paramref name="generator"/>.</summary>
    public GeneratorDigest(VoxelGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
    }

    /// <summary>One number for a list of digests: XxHash64 over (lod, x, y, z, sdf, material) of each, little endian.</summary>
    public static ulong Combine(IEnumerable<BlockDigest> digests)
    {
        var hash = new XxHash64();
        Span<byte> bytes = stackalloc byte[32];
        foreach (var d in digests)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, d.Block.Lod);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], d.Block.X);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], d.Block.Y);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], d.Block.Z);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], d.Sdf);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], d.Material);
            hash.Append(bytes);
        }

        return hash.GetCurrentHashAsUInt64();
    }

    /// <summary>Generates the block and hashes its channels (uniform channels are expanded by the module, so equal data gives equal hashes).</summary>
    public BlockDigest Compute(BlockSample block)
    {
        _buffer.Create(BlockSample.BlockSize, BlockSample.BlockSize, BlockSample.BlockSize);
        _format.ConfigureBuffer(_buffer);
        var (x, y, z) = block.Origin;
        _generator.GenerateBlock(_buffer, new Vector3(x, y, z), block.Lod);
        var uniform = _buffer.IsUniform((int)TerrainChannels.Sdf);
        var sdf = XxHash64.HashToUInt64(_buffer.GetChannelAsByteArray(TerrainChannels.Sdf));
        var material = XxHash64.HashToUInt64(_buffer.GetChannelAsByteArray(TerrainChannels.Material));
        return new BlockDigest(block, sdf, material, uniform);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer.Dispose();
        _format.Dispose();
    }
}
