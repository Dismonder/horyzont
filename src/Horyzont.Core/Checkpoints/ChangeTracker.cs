using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Checkpoints;

/// <summary>Keys changed since the last checkpoint. Order does not matter: capture sorts them.</summary>
internal sealed class ChangeTracker
{
    internal HashSet<EntityId> Constructs { get; } = [];

    internal HashSet<ConstructChunkKey> Chunks { get; } = [];

    internal HashSet<PlayerId> Players { get; } = [];

    internal HashSet<TerrainBlockKey> Terrain { get; } = [];

    internal HashSet<MarkerId> Markers { get; } = [];
    internal HashSet<EntityId> ShipStates { get; } = [];
    internal HashSet<ContainerId> Containers { get; } = [];

    internal HashSet<BlockRef> BlockEntities { get; } = [];

    internal HashSet<PlayerId> Progress { get; } = [];

    /// <summary>Gets a value indicating whether the progress keys of the world changed (K6-32).</summary>
    internal bool ProgressKeys { get; private set; }

    internal void MarkConstruct(EntityId id) => Constructs.Add(id);

    internal void MarkChunk(ConstructChunkKey key) => Chunks.Add(key);

    internal void MarkPlayer(PlayerId id) => Players.Add(id);

    internal void MarkTerrain(TerrainBlockKey key) => Terrain.Add(key);

    internal void MarkMarker(MarkerId id) => Markers.Add(id);
    internal void MarkShipState(EntityId id) => ShipStates.Add(id);
    internal void MarkContainer(ContainerId id) => Containers.Add(id);

    internal void MarkBlockEntity(BlockRef key) => BlockEntities.Add(key);

    internal void MarkProgress(PlayerId id) => Progress.Add(id);

    internal void MarkProgressKeys() => ProgressKeys = true;

    internal void Clear()
    {
        Constructs.Clear();
        Chunks.Clear();
        Players.Clear();
        Terrain.Clear();
        Markers.Clear();
        ShipStates.Clear();
        Containers.Clear();
        BlockEntities.Clear();
        Progress.Clear();
        ProgressKeys = false;
    }
}
