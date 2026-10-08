using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// State of entities around the client, as deltas against <paramref name="BaselineSnapshotId"/> (0 = full).
/// Entities that do not fit are skipped and prioritized in the next snapshot; skipping is not despawning.
/// </summary>
/// <param name="SnapshotId">Snapshot number.</param>
/// <param name="ServerTick">Tick.</param>
/// <param name="BaselineSnapshotId">Baseline snapshot; 0 for a full snapshot.</param>
/// <param name="LastProcessedInputSeq">Last processed input of the client.</param>
/// <param name="Frames">1–8 frames referenced by index.</param>
/// <param name="Entities">Entity states.</param>
public sealed record Snapshot(
    uint SnapshotId, Tick ServerTick, uint BaselineSnapshotId, uint LastProcessedInputSeq, IReadOnlyList<SnapshotFrame> Frames,
    IReadOnlyList<EntityState> Entities) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.Snapshot;
}
