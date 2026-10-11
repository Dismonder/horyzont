using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Terrain;

/// <summary>
/// Picks the generated blocks whose digest the client reports to the server: every five seconds up to 32 of them, rotating through the
/// candidates in key order so that a full cycle covers all of them.
/// </summary>
/// <param name="time">Clock of the interval.</param>
public sealed class TerrainChecksumScheduler(TimeProvider time)
{
    /// <summary>Candidates per report.</summary>
    public const int BatchSize = 32;

    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private TerrainBlockKey? _cursor;
    private long _lastAt;
    private bool _started;

    /// <summary>Gets a value indicating whether the next batch is due.</summary>
    public bool IsDue => !_started || _time.GetElapsedTime(_lastAt) >= _interval;

    /// <summary>Takes the next batch when it is due.</summary>
    /// <param name="sortedCandidates">Candidate blocks (generated, not predicted), sorted by key.</param>
    /// <param name="batch">Receives the chosen keys; cleared first.</param>
    /// <returns>Whether a batch was taken.</returns>
    public bool TryTake(IReadOnlyList<TerrainBlockKey> sortedCandidates, List<TerrainBlockKey> batch)
    {
        ArgumentNullException.ThrowIfNull(sortedCandidates);
        ArgumentNullException.ThrowIfNull(batch);
        batch.Clear();
        if (!IsDue || sortedCandidates.Count == 0)
        {
            return false;
        }

        var start = 0;
        if (_cursor is { } cursor)
        {
            while (start < sortedCandidates.Count && sortedCandidates[start] <= cursor)
            {
                start++;
            }

            if (start >= sortedCandidates.Count)
            {
                start = 0;
            }
        }

        var count = Math.Min(BatchSize, sortedCandidates.Count);
        for (var i = 0; i < count; i++)
        {
            batch.Add(sortedCandidates[(start + i) % sortedCandidates.Count]);
        }

        _cursor = batch[^1];
        _lastAt = _time.GetTimestamp();
        _started = true;
        return true;
    }
}
