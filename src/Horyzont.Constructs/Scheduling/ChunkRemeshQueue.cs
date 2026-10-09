using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;

namespace Horyzont.Constructs.Scheduling;

/// <summary>Distance from the nearest viewer to a construct, in metres squared; smaller is served first.</summary>
/// <param name="construct">Construct entity.</param>
/// <returns>Squared distance.</returns>
public delegate double ConstructDistance(EntityId construct);

/// <summary>
/// Main-thread bookkeeping of chunk rebuilds: a set of dirty chunks (deduplicated), at most <c>maxInFlight</c> started jobs and a
/// generation per chunk. A result is accepted only when its generation is still the latest one, so edits made while a job runs and
/// removed constructs never apply stale meshes. Contains no clock and no threads.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name fixed by the K2-12 plan; the type is a scheduler, not a collection.")]
public sealed class ChunkRemeshQueue
{
    private readonly int _maxInFlight;
    private readonly Dictionary<ConstructChunkKey, RemeshPriority> _dirty = [];
    private readonly Dictionary<ConstructChunkKey, uint> _generation = [];
    private readonly HashSet<ConstructChunkKey> _running = [];
    private readonly List<ConstructChunkKey> _scratch = [];
    private uint _counter;

    /// <summary>Creates a queue.</summary>
    /// <param name="maxInFlight">Upper bound of started jobs that have not completed (job and result queues).</param>
    public ChunkRemeshQueue(int maxInFlight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInFlight, 1);
        _maxInFlight = maxInFlight;
    }

    /// <summary>Optional distance used to order jobs of equal priority; null treats all constructs as equally near.</summary>
    public ConstructDistance? Distance { get; set; }

    /// <summary>Started jobs that have not completed.</summary>
    public int InFlight => _running.Count;

    /// <summary>Chunks waiting to be started.</summary>
    public int DirtyCount => _dirty.Count;

    /// <summary>Dirty plus running chunks.</summary>
    public int Pending => _dirty.Count + _running.Count;

    /// <summary>Marks a chunk for rebuilding; repeated marks collapse, <see cref="RemeshPriority.Edit"/> upgrades a waiting rebuild.</summary>
    /// <param name="key">Chunk.</param>
    /// <param name="priority">Priority of this request.</param>
    public void MarkDirty(ConstructChunkKey key, RemeshPriority priority)
    {
        _generation[key] = ++_counter;
        if (_dirty.TryGetValue(key, out var existing))
        {
            _dirty[key] = (RemeshPriority)Math.Min((int)existing, (int)priority);
        }
        else
        {
            _dirty.Add(key, priority);
        }
    }

    /// <summary>Starts the most urgent waiting job, if the in-flight bound allows one.</summary>
    /// <param name="job">The started job.</param>
    /// <returns>False when nothing is waiting or the bound is reached.</returns>
    public bool TryStartNext(out RemeshJob job)
    {
        job = default;
        if (_running.Count >= _maxInFlight || _dirty.Count == 0)
        {
            return false;
        }

        var found = false;
        var best = default(ConstructChunkKey);
        var bestPriority = RemeshPriority.Rebuild;
        var bestDistance = 0.0;
        foreach (var (key, priority) in _dirty)
        {
            if (_running.Contains(key))
            {
                continue; // wait for the running job of the same chunk; its result is stale anyway
            }

            var distance = Distance?.Invoke(key.Construct) ?? 0.0;
            if (!found || IsBetter(key, priority, distance, best, bestPriority, bestDistance))
            {
                found = true;
                best = key;
                bestPriority = priority;
                bestDistance = distance;
            }
        }

        if (!found)
        {
            return false;
        }

        _dirty.Remove(best);
        _running.Add(best);
        job = new RemeshJob(best, _generation[best], bestPriority);
        return true;
    }

    /// <summary>Completes a job.</summary>
    /// <param name="job">The job from <see cref="TryStartNext"/>.</param>
    /// <returns>True when the result is current and must be applied; false when it must be dropped.</returns>
    public bool Complete(RemeshJob job)
    {
        _running.Remove(job.Key);
        return IsCurrent(job);
    }

    /// <summary>Checks whether a result is still current, including after mesh upload and before collision upload.</summary>
    /// <param name="job">The job that produced the result.</param>
    /// <returns>True when its generation has not been invalidated.</returns>
    public bool IsCurrent(RemeshJob job) => _generation.TryGetValue(job.Key, out var latest) && latest == job.Generation;

    /// <summary>Forgets every chunk of a construct: waiting entries are dropped and running jobs become stale.</summary>
    /// <param name="construct">Removed construct.</param>
    public void RemoveConstruct(EntityId construct)
    {
        _scratch.Clear();
        foreach (var key in _dirty.Keys)
        {
            if (key.Construct == construct)
            {
                _scratch.Add(key);
            }
        }

        foreach (var key in _scratch)
        {
            _dirty.Remove(key);
        }

        _scratch.Clear();
        foreach (var key in _generation.Keys)
        {
            if (key.Construct == construct)
            {
                _scratch.Add(key);
            }
        }

        foreach (var key in _scratch)
        {
            _generation.Remove(key);
        }

        _scratch.Clear();
    }

    /// <summary>Drops everything: waiting entries, generations (running jobs become stale). Used when the range changes or at shutdown.</summary>
    public void Clear()
    {
        _dirty.Clear();
        _generation.Clear();
    }

    private static bool IsBetter(
        ConstructChunkKey key, RemeshPriority priority, double distance, ConstructChunkKey best, RemeshPriority bestPriority, double bestDistance)
    {
        if (priority != bestPriority)
        {
            return priority < bestPriority;
        }

        if (distance != bestDistance)
        {
            return distance < bestDistance;
        }

        return ConstructChunkKeyComparer.Instance.Compare(key, best) < 0;
    }
}
