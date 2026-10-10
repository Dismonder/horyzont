using Horyzont.Core.Ids;

namespace Horyzont.Client.Replication;

/// <summary>
/// Entities that await a server answer to a resync request. An unanswered request is repeated with growing delays (2, 4, 8, then 16 s),
/// because the server may refuse a request (rate limit) and a refusal is silent.
/// </summary>
internal sealed class ResyncTracker(TimeProvider time)
{
    private static readonly TimeSpan _firstDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _maxDelay = TimeSpan.FromSeconds(16);
    private readonly Dictionary<EntityId, Entry> _entries = [];
    private readonly List<EntityId> _scratch = [];

    public int Count => _entries.Count;

    public bool Contains(EntityId id) => _entries.ContainsKey(id);

    /// <summary>Starts waiting for an answer; false when the entity was already waiting.</summary>
    public bool Add(EntityId id) => _entries.TryAdd(id, new Entry(time.GetTimestamp(), 0, false));

    public bool Remove(EntityId id) => _entries.Remove(id);

    public void Clear() => _entries.Clear();

    /// <summary>Marks the first part of the answer as received: the transfer's own stall timer takes over.</summary>
    public void MarkAnswered(EntityId id)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            _entries[id] = entry with { Answered = true };
        }
    }

    /// <summary>Appends the entities whose request must be repeated now and schedules their next attempt.</summary>
    public void CollectDue(List<EntityId> into)
    {
        _scratch.Clear();
        var now = time.GetTimestamp();
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            if (!entry.Answered && time.GetElapsedTime(entry.Last, now) >= Delay(entry.Attempt))
            {
                _scratch.Add(pair.Key);
            }
        }

        foreach (var id in _scratch)
        {
            var entry = _entries[id];
            _entries[id] = entry with { Last = now, Attempt = entry.Attempt + 1 };
            into.Add(id);
        }
    }

    private static TimeSpan Delay(int attempt)
    {
        var delay = _firstDelay * (1L << Math.Min(attempt, 3));
        return delay > _maxDelay ? _maxDelay : delay;
    }

    private readonly record struct Entry(long Last, int Attempt, bool Answered);
}
