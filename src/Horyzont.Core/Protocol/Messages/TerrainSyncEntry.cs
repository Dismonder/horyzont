using Horyzont.Core.World.Terrain;
namespace Horyzont.Core.Protocol.Messages;
/// <summary>Authoritative LOD zero version and canonical voxel digest.</summary>
/// <param name="Key">Block key.</param>
/// <param name="Version">Authoritative version, including generator correction zero.</param>
/// <param name="Hash">Canonical voxel digest.</param>
public readonly record struct TerrainSyncEntry(TerrainBlockKey Key, uint Version, ulong Hash);
