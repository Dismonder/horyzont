using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Hash of one terrain block.</summary>
/// <param name="Key">Block key.</param>
/// <param name="Hash">Hash of its voxels.</param>
public readonly record struct TerrainBlockChecksum(TerrainBlockKey Key, ulong Hash);
