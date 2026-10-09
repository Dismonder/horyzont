namespace Horyzont.Core.Serialization;

/// <summary>Decoded terrain block blob: voxel payload format and the payload, opaque to Core.</summary>
/// <remarks>
/// Equality is by reference for <see cref="Voxels"/> (the generated record equality compares the array reference, not its content):
/// callers must compare <c>Voxels</c> with <c>SequenceEqual</c>.
/// </remarks>
/// <param name="VoxelFormat">Format of <paramref name="Voxels"/> defined by the terrain adapter; never 0.</param>
/// <param name="Voxels">The serialized voxels.</param>
public sealed record TerrainBlockBlob(byte VoxelFormat, byte[] Voxels);
