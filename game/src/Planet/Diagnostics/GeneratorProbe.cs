using Godot;

namespace Horyzont.Game.Planet.Diagnostics;

/// <summary>
/// Samples single voxels of a generator through <c>GenerateBlock</c> with a 1x1x1 buffer (Voxel Tools 1.7 has no
/// <c>generate_single</c> in its script API). Not thread safe; one probe per thread.
/// </summary>
public sealed class GeneratorProbe : IDisposable
{
    private readonly VoxelGenerator _generator;
    private readonly VoxelBuffer _buffer = new();
    private readonly VoxelFormat _format = TerrainChannels.CreateFormat();

    /// <summary>Creates a probe for <paramref name="generator"/>.</summary>
    public GeneratorProbe(VoxelGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
    }

    /// <summary>SDF and texture index of the voxel at integer position (x, y, z) at LOD 0.</summary>
    public (float Sdf, byte Material) Sample(int x, int y, int z)
    {
        _buffer.Create(1, 1, 1);
        _format.ConfigureBuffer(_buffer);
        _generator.GenerateBlock(_buffer, new Vector3(x, y, z), 0);
        return ((float)_buffer.GetVoxelF(0, 0, 0, (uint)TerrainChannels.Sdf), (byte)_buffer.GetVoxel(0, 0, 0, (uint)TerrainChannels.Material));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer.Dispose();
        _format.Dispose();
    }
}
