namespace Horyzont.Persistence.Writing;

/// <summary>
/// The checkpoint was not written; the server loop calls <c>WorldState.RequeueAfterFailedCheckpoint(Request.Core)</c> and hands the
/// keys of <see cref="CheckpointRequest.TerrainBlocks"/> back to the terrain adapter.
/// </summary>
/// <param name="Request">The request.</param>
public sealed record CheckpointFailed(CheckpointRequest Request) : PersistenceEvent;
