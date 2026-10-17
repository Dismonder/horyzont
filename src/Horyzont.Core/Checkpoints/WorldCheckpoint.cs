using Horyzont.Core.Ids;
using Horyzont.Core.World.Markers;
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
/// <param name="UpsertedMarkers">Markers that exist and changed, by id (K5-27).</param>
/// <param name="RemovedMarkers">Markers removed, by id (K5-27).</param>
/// <param name="NextMarkerId">Next marker id (K5-27); 1 when no marker was ever created.</param>
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
    IReadOnlyList<MarkerRecord> UpsertedMarkers,
    IReadOnlyList<MarkerId> RemovedMarkers,
    ulong NextMarkerId,
    IReadOnlyList<ShipStateRecord>? UpsertedShipStates = null,
    IReadOnlyList<EntityId>? RemovedShipStates = null)
{
    /// <summary>Creates a checkpoint without marker changes (worlds and tests that predate K5-27).</summary>
    public WorldCheckpoint(
        OpId barrierOpId,
        Tick tick,
        ulong nextEntityId,
        ulong nextPlayerId,
        IReadOnlyList<ConstructHeaderRecord> upsertedConstructs,
        IReadOnlyList<EntityId> removedConstructs,
        IReadOnlyList<ConstructChunkRecord> upsertedChunks,
        IReadOnlyList<ConstructChunkKey> removedChunks,
        IReadOnlyList<PlayerRecord> upsertedPlayers,
        IReadOnlyList<TerrainBlockVersion> terrainVersions)
        : this(
            barrierOpId, tick, nextEntityId, nextPlayerId, upsertedConstructs, removedConstructs, upsertedChunks, removedChunks,
            upsertedPlayers, terrainVersions, [], [], 1)
    {
    }

    /// <summary>Gets the upserted ship states, never null.</summary>
    public IReadOnlyList<ShipStateRecord> ShipStatesToWrite => UpsertedShipStates ?? [];

    /// <summary>Gets the removed ship states, never null.</summary>
    public IReadOnlyList<EntityId> ShipStatesToDelete => RemovedShipStates ?? [];

    /// <summary>Next container id (<c>next_container_id</c>).</summary>
    public ulong NextContainerId { get; init; } = 1;

    /// <summary>Containers that exist and changed, by id.</summary>
    public IReadOnlyList<ContainerRecord> UpsertedContainers { get; init; } = [];

    /// <summary>Containers removed, by id.</summary>
    public IReadOnlyList<ContainerId> RemovedContainers { get; init; } = [];
}
