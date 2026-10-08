using Horyzont.Core.Checkpoints;

namespace Horyzont.Persistence.Writing;

/// <summary>
/// A checkpoint to write: the captured core changes and the voxel data of terrain blocks copied by the game. Every entry of
/// <see cref="WorldCheckpoint.TerrainVersions"/> needs a payload with the same version; more payloads (blocks unloaded since the
/// previous checkpoint) are allowed.
/// </summary>
/// <param name="Core">Result of <c>WorldState.CaptureCheckpoint</c>.</param>
/// <param name="TerrainBlocks">Voxel data of changed terrain blocks.</param>
public sealed record CheckpointRequest(WorldCheckpoint Core, IReadOnlyList<TerrainBlockPayload> TerrainBlocks);
