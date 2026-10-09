using Godot;

namespace Horyzont.Game.Planet;

/// <summary>
/// Voxel channel layout of planet terrain, shared by the generator, the terrain stream and the block codec. Append only.
/// SDF: 16-bit (smooth surface). Material: 8-bit texture index per voxel (<c>VoxelMesherTransvoxel.TexturingMode.SingleS4</c>).
/// </summary>
public static class TerrainChannels
{
    /// <summary>Signed distance field, negative inside matter.</summary>
    public const VoxelBuffer.ChannelId Sdf = VoxelBuffer.ChannelId.ChannelSdf;

    /// <summary>Texture index of the voxel material (see <see cref="TerrainMaterials"/>).</summary>
    public const VoxelBuffer.ChannelId Material = VoxelBuffer.ChannelId.ChannelIndices;

    /// <summary>Bit mask of the channels terrain blocks use (for the used-channels mask of a terrain stream).</summary>
    public const int UsedChannelsMask = (1 << (int)Sdf) | (1 << (int)Material);

    /// <summary>Depth of the SDF channel.</summary>
    public const VoxelBuffer.Depth SdfDepth = VoxelBuffer.Depth.Depth16Bit;

    /// <summary>Depth of the material channel.</summary>
    public const VoxelBuffer.Depth MaterialDepth = VoxelBuffer.Depth.Depth8Bit;

    /// <summary>Creates the format resource assigned to the terrain and used to configure buffers.</summary>
    public static VoxelFormat CreateFormat()
    {
        var format = new VoxelFormat();
        format.SetChannelDepth(Sdf, SdfDepth);
        format.SetChannelDepth(Material, MaterialDepth);
        return format;
    }
}
