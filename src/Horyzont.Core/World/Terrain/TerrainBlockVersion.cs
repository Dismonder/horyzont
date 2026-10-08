namespace Horyzont.Core.World.Terrain;

/// <summary>Version of an edited terrain block.</summary>
/// <param name="Key">Block key.</param>
/// <param name="Version">Version, incremented by every edit touching the block; 0 = never edited.</param>
public readonly record struct TerrainBlockVersion(TerrainBlockKey Key, uint Version);
