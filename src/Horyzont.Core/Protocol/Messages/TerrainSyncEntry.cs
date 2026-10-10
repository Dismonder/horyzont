using Horyzont.Core.World.Terrain;
namespace Horyzont.Core.Protocol.Messages;
/// <summary>Authoritative LOD zero version and the <see cref="TerrainBlockHash"/> of its envelope.</summary>
/// <param name="Key">Block key.</param>
/// <param name="Version">Authoritative version, including generator correction zero.</param>
/// <param name="Hash">Hash of the envelope the server serves for that version; zero for version zero without data.</param>
public readonly record struct TerrainSyncEntry(TerrainBlockKey Key, uint Version, ulong Hash);
