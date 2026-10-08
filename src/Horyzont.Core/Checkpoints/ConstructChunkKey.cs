using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Checkpoints;

/// <summary>Key of a construct chunk.</summary>
/// <param name="Construct">Construct entity.</param>
/// <param name="Chunk">Chunk position.</param>
public readonly record struct ConstructChunkKey(EntityId Construct, ChunkPos Chunk);
