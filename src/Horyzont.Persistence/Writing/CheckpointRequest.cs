using Horyzont.Core.Checkpoints;

namespace Horyzont.Persistence.Writing;

/// <summary>
/// A checkpoint to write: the captured core changes and the voxel data of terrain blocks copied by the game. The payloads are
/// exactly the entries of <see cref="WorldCheckpoint.TerrainVersions"/>, one per key with the same version; any other payload would
/// write a block state that does not belong to the barrier.
/// </summary>
/// <param name="Core">Result of <c>WorldState.CaptureCheckpoint</c>.</param>
/// <param name="TerrainBlocks">Voxel data of changed terrain blocks.</param>
public sealed record CheckpointRequest(WorldCheckpoint Core, IReadOnlyList<TerrainBlockPayload> TerrainBlocks);
