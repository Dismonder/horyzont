using Godot;

using Horyzont.Core.Operations;
using Horyzont.Game.Planet;

namespace Horyzont.Game.Terrain;

/// <summary>
/// The sphere edit of the terrain with the parameters of <see cref="TerrainEditSemantics"/>: the one place that changes voxels for an
/// edit, shared by the server terrain service and the prediction of a remote client, so both give the same voxels from the same blocks.
/// </summary>
internal static class TerrainSphereEdit
{
    /// <summary>Applies the edit to the voxels of the terrain the tool belongs to. Main thread.</summary>
    /// <param name="tool">The voxel tool of the planet terrain.</param>
    /// <param name="mode">Add or remove.</param>
    /// <param name="center">Centre of the sphere in voxels (metres of a planet with 1 m voxels).</param>
    /// <param name="radius">Radius in metres.</param>
    /// <param name="textureIndex">Texture index written by an add (the material of the world mapped by <see cref="TerrainMaterialMap"/>).</param>
    internal static void Apply(VoxelTool tool, TerrainEditMode mode, Vector3 center, float radius, byte textureIndex)
    {
        tool.Channel = TerrainChannels.Sdf;
        tool.Mode = mode == TerrainEditMode.Add ? VoxelTool.ModeEnum.Add : VoxelTool.ModeEnum.Remove;
        tool.SdfScale = TerrainEditSemantics.SdfScale;
        tool.SdfStrength = TerrainEditSemantics.SdfStrength;
        tool.DoSphere(center, radius);
        if (mode == TerrainEditMode.Add)
        {
            tool.Channel = TerrainChannels.Material;
            tool.Value = textureIndex;
            tool.DoSphere(center, radius + TerrainEditSemantics.MaterialSphereRadiusOffset);
            tool.Channel = TerrainChannels.Sdf;
        }
    }
}
