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

    internal void MarkConstruct(EntityId id) => Constructs.Add(id);

    internal void MarkChunk(ConstructChunkKey key) => Chunks.Add(key);

    internal void MarkPlayer(PlayerId id) => Players.Add(id);

    internal void MarkTerrain(TerrainBlockKey key) => Terrain.Add(key);

    internal void MarkMarker(MarkerId id) => Markers.Add(id);

    internal void Clear()
    {
        Constructs.Clear();
        Chunks.Clear();
        Players.Clear();
        Terrain.Clear();
        Markers.Clear();
    }
}
