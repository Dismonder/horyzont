using Horyzont.Core.Checkpoints;

namespace Horyzont.Constructs.Scheduling;

/// <summary>A started rebuild of one chunk.</summary>
/// <param name="Key">Chunk.</param>
/// <param name="Generation">Generation of the request; the result is dropped when a newer one exists.</param>
/// <param name="Priority">Priority the job was started with.</param>
public readonly record struct RemeshJob(ConstructChunkKey Key, uint Generation, RemeshPriority Priority);
