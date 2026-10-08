namespace Horyzont.Persistence.Writing;

/// <summary>The checkpoint is durable; the server loop calls <c>WorldState.CompleteCheckpoint(Request.Core)</c>.</summary>
/// <param name="Request">The request.</param>
public sealed record CheckpointCommitted(CheckpointRequest Request) : PersistenceEvent;
