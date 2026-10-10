using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Server.Replication;

/// <summary>A main-thread copy of the replication-relevant state of one entity.</summary>
/// <param name="Id">Entity identifier.</param>
/// <param name="Kind">Entity kind.</param>
/// <param name="Owner">Player owning the entity.</param>
/// <param name="Frame">Parent reference and its sequence.</param>
/// <param name="State">Complete local state.</param>
/// <param name="BoundsRadius">Interest bounding sphere radius.</param>
public readonly record struct ReplicationEntity(EntityId Id, EntityKind Kind, PlayerId? Owner,
    SnapshotFrame Frame, EntityState State, float BoundsRadius);
