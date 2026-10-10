namespace Horyzont.Server.Replication;

/// <summary>Deterministic allocation-free ordering for a bounded snapshot, including starvation prevention.</summary>
public static class SnapshotSchedulingPolicy
{
    /// <summary>Selects own player first, starved entities next, then distance divided by age.</summary>
    /// <param name="candidates">Costs computed by the encoder; caller updates ages after sending.</param>
    /// <param name="selectedIndices">Receives candidate indices in send order.</param>
    /// <param name="byteBudget">Available bytes after subtracting the message header.</param>
    /// <returns>Number of selected entries; costs never exceed the budget.</returns>
    public static int Select(ReadOnlySpan<SnapshotCandidate> candidates, Span<int> selectedIndices, int byteBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteBudget);
        var count = 0;
        while (count < selectedIndices.Length)
        {
            var best = -1;
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate.EncodedBytes <= 0 || selectedIndices[..count].Contains(i))
                {
                    continue;
                }

                if (candidate.IsOwnPlayer && count == 0 && candidate.EncodedBytes > byteBudget)
                {
                    return 0;
                }

                if (candidate.EncodedBytes > byteBudget)
                {
                    continue;
                }

                if (best < 0 || Compare(candidate, candidates[best]) < 0)
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            selectedIndices[count++] = best;
            byteBudget -= candidates[best].EncodedBytes;
        }

        return count;
    }

    private static int Compare(in SnapshotCandidate left, in SnapshotCandidate right)
    {
        var order = right.IsOwnPlayer.CompareTo(left.IsOwnPlayer);
        if (order != 0)
        {
            return order;
        }

        var leftStarved = left.OmittedRounds >= 10;
        var rightStarved = right.OmittedRounds >= 10;
        order = rightStarved.CompareTo(leftStarved);
        if (order != 0)
        {
            return order;
        }

        if (leftStarved)
        {
            order = right.OmittedRounds.CompareTo(left.OmittedRounds);
        }
        else
        {
            var leftKey = Math.Clamp(float.IsFinite(left.Distance) ? left.Distance : 550f, 1f, 550f) / (1d + Math.Max(0, left.OmittedRounds));
            var rightKey = Math.Clamp(float.IsFinite(right.Distance) ? right.Distance : 550f, 1f, 550f) / (1d + Math.Max(0, right.OmittedRounds));
            order = leftKey.CompareTo(rightKey);
        }

        return order != 0 ? order : left.Entity.Value.CompareTo(right.Entity.Value);
    }
}
