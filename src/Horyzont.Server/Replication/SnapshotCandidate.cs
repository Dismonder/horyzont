using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>Encoded entity cost and its starvation age supplied by the snapshot builder.</summary>
/// <param name="Entity">Entity identifier, used to break ties.</param>
/// <param name="Distance">Distance in metres.</param>
/// <param name="EncodedBytes">Actual encoded field and frame contribution.</param>
/// <param name="OmittedRounds">Consecutive rounds omitted.</param>
/// <param name="IsOwnPlayer">Whether this is the receiving client's own body or a construct of its frame chain (K5-26: always sent first).</param>
public readonly record struct SnapshotCandidate(EntityId Entity, float Distance, int EncodedBytes, int OmittedRounds, bool IsOwnPlayer);
