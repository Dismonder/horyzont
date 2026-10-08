using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Terrain;

/// <summary>Terrain geometry helpers shared by the simulation and the terrain adapter.</summary>
public static class TerrainMath
{
    /// <summary>Voxels per terrain data block edge.</summary>
    public const int BlockSize = 16;

    /// <summary>Normalized direction from the planet centre (the frame origin); +Y at the centre itself.</summary>
    /// <param name="planetLocalPosition">Position in the planet frame.</param>
    /// <returns>The up direction.</returns>
    public static Vector3 UpAt(Vector3 planetLocalPosition)
    {
        var length = planetLocalPosition.Length();
        return length > 0 && float.IsFinite(length) ? planetLocalPosition / length : Vector3.UnitY;
    }

    /// <summary>
    /// LOD0 block keys whose voxels can change: the bounding box of the sphere grown by <paramref name="marginVoxels"/>,
    /// block coordinates = floor(position / (16 * voxelSize)). The output is cleared and filled in sorted order.
    /// </summary>
    /// <param name="planet">Planet.</param>
    /// <param name="center">Sphere centre in the planet frame.</param>
    /// <param name="radius">Sphere radius in metres.</param>
    /// <param name="voxelSize">Voxel size in metres.</param>
    /// <param name="marginVoxels">Margin in voxels.</param>
    /// <param name="output">Receives the keys.</param>
    public static void AffectedBlocks(
        PlanetId planet, Vector3 center, float radius, float voxelSize, int marginVoxels, List<TerrainBlockKey> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        var blockMeters = BlockSize * voxelSize;
        var extent = radius + (marginVoxels * voxelSize);
        var minX = (int)MathF.Floor((center.X - extent) / blockMeters);
        var minY = (int)MathF.Floor((center.Y - extent) / blockMeters);
        var minZ = (int)MathF.Floor((center.Z - extent) / blockMeters);
        var maxX = (int)MathF.Floor((center.X + extent) / blockMeters);
        var maxY = (int)MathF.Floor((center.Y + extent) / blockMeters);
        var maxZ = (int)MathF.Floor((center.Z + extent) / blockMeters);
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    output.Add(new TerrainBlockKey(planet, 0, x, y, z));
                }
            }
        }
    }
}
