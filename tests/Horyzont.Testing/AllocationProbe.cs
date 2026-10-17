namespace Horyzont.Testing;

/// <summary>Allocation measurement for hot paths that tolerates rare runtime allocations on the test thread.</summary>
public static class AllocationProbe
{
    /// <summary>
    /// Runs <paramref name="body"/> in <paramref name="rounds"/> rounds and returns the fewest bytes the current thread allocated in one round.
    /// Code that allocates on its path allocates in every round. A one-off runtime allocation on a loaded CI host lands in a single round,
    /// which made GitHub-hosted runs report a few hundred bytes for allocation-free code.
    /// </summary>
    public static long MinAllocatedBytes(Action body, int rounds = 3)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
        var fewest = long.MaxValue;
        for (var round = 0; round < rounds && fewest != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            body();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return fewest;
    }
}
