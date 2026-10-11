using Godot;

using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;

namespace Horyzont.Game.Terrain;

/// <summary>
/// The canonical generator block of a planet as the K2 envelope and as its voxel digest: what a client computes for a block nobody
/// edited, and what the server sends to repair a difference. Main thread only (one scratch buffer).
/// </summary>
public sealed class GeneratedBlockSource : IDisposable
{
    private readonly VoxelGenerator _generator;
    private readonly TerrainBlockCodec _codec;
    private readonly VoxelBuffer _buffer = new();
    private readonly VoxelFormat _format = TerrainChannels.CreateFormat();

    /// <summary>Creates the source.</summary>
    /// <param name="generator">Generator of the planet terrain.</param>
    /// <param name="codec">Envelope codec; it is not owned.</param>
    public GeneratedBlockSource(VoxelGenerator generator, TerrainBlockCodec codec)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(codec);
        _generator = generator;
        _codec = codec;
    }

    /// <summary>Generates a LOD 0 block and encodes it.</summary>
    /// <param name="key">Block key.</param>
    /// <returns>A new envelope.</returns>
    public byte[] Encode(TerrainBlockKey key)
    {
        Generate(key);
        return _codec.Encode(_buffer);
    }

    /// <summary>Generates a LOD 0 block and computes its voxel digest.</summary>
    /// <param name="key">Block key.</param>
    /// <returns>The digest as <see cref="TerrainBlockCodec.Digest"/> defines it.</returns>
    public ulong Digest(TerrainBlockKey key)
    {
        Generate(key);
        return _codec.Digest(_buffer);
    }

    /// <summary>Generates a block into a caller buffer (the buffer is resized and formatted).</summary>
    /// <param name="key">Block key (any LOD).</param>
    /// <param name="into">Receives the block.</param>
    public void GenerateInto(TerrainBlockKey key, VoxelBuffer into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Create(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
        _format.ConfigureBuffer(into);
        _generator.GenerateBlock(into, Origin(key), key.Lod);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer.Dispose();
        _format.Dispose();
    }

    private static Vector3 Origin(TerrainBlockKey key)
    {
        // The origin of a block of LOD n is in voxels of LOD 0: block coordinate times 16 voxels times 2^n.
        var edge = (double)TerrainMath.BlockSize * (1 << key.Lod);
        return new Vector3(key.X * edge, key.Y * edge, key.Z * edge);
    }

    private void Generate(TerrainBlockKey key) => GenerateInto(key, _buffer);
}
