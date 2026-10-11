using Horyzont.Core.Ids;
using Horyzont.Core.World.Ships;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Checkpoints;

/// <summary>Changes of the world state since the previous checkpoint.</summary>
/// <param name="BarrierOpId">Last operation covered by the checkpoint.</param>
/// <param name="Tick">Tick of the capture.</param>
/// <param name="NextEntityId">Next persistent entity id.</param>
/// <param name="NextPlayerId">Next player id.</param>
/// <param name="UpsertedConstructs">Constructs that exist and changed, by id.</param>
/// <param name="RemovedConstructs">Constructs removed, by id.</param>
/// <param name="UpsertedChunks">Chunks that exist and changed, by key.</param>
/// <param name="RemovedChunks">Chunks removed, by key.</param>
/// <param name="UpsertedPlayers">Changed players and every online player, by id.</param>
/// <param name="TerrainVersions">Changed terrain block versions (voxel data is added by the terrain adapter, package 9).</param>
/// <param name="UpsertedShipStates">Changed flight states of existing ships, by construct id (K5-24); null = none.</param>
/// <param name="RemovedShipStates">Flight states of constructs that no longer exist, by id (K5-24); null = none.</param>
public sealed record WorldCheckpoint(
    OpId BarrierOpId,
    Tick Tick,
    ulong NextEntityId,
    ulong NextPlayerId,
    IReadOnlyList<ConstructHeaderRecord> UpsertedConstructs,
    IReadOnlyList<EntityId> RemovedConstructs,
    IReadOnlyList<ConstructChunkRecord> UpsertedChunks,
    IReadOnlyList<ConstructChunkKey> RemovedChunks,
    IReadOnlyList<PlayerRecord> UpsertedPlayers,
    IReadOnlyList<TerrainBlockVersion> TerrainVersions,
    IReadOnlyList<ShipStateRecord>? UpsertedShipStates = null,
    IReadOnlyList<EntityId>? RemovedShipStates = null)
{
    /// <summary>Gets the upserted ship states, never null.</summary>
    public IReadOnlyList<ShipStateRecord> ShipStatesToWrite => UpsertedShipStates ?? [];

    /// <summary>Gets the removed ship states, never null.</summary>
    public IReadOnlyList<EntityId> ShipStatesToDelete => RemovedShipStates ?? [];
}
