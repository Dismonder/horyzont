using Horyzont.Core.World.Terrain;

namespace Horyzont.Persistence.Writing;

/// <summary>Voxel data of one terrain block, compressed by the game adapter.</summary>
/// <param name="Key">Block key.</param>
/// <param name="Version">Block version the data belongs to.</param>
/// <param name="Data">The data; owned by the record.</param>
#pragma warning disable CA1819 // The record owns the array; the writer stores it as a blob.
public sealed record TerrainBlockPayload(TerrainBlockKey Key, uint Version, byte[] Data);
#pragma warning restore CA1819
