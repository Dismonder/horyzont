using Horyzont.Bots.Client;
using Horyzont.Bots.Replicas;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Bots.Runner;

/// <summary>
/// Measures how long an edit takes to become visible at the other bots (K4-19, P5): from the moment the author submitted the intent
/// (<see cref="IntentRecord.SentAt"/>) to the moment another bot received the change. Block operations are matched by <see cref="OpId"/>
/// (<c>OperationApplied</c> seen by the construct mirror); terrain edits by (block key, version), which the author learns from the barrier
/// of its own edit. Events may arrive in any order; <see cref="Resolve"/> pairs them and forgets what is older than the horizon, so a
/// long run keeps bounded memory. All timestamps come from one clock.
/// </summary>
public sealed class EditLatencyTracker
{
    private readonly TimeProvider _time;
    private readonly long _horizon;
    private readonly Dictionary<OpId, (int Bot, long SentAt, long At)> _sent = [];
    private readonly Dictionary<(TerrainBlockKey Key, uint Version), (OpId Op, long At)> _versions = [];
    private readonly List<(int Bot, OpId Op, long At)> _seenOps = [];
    private readonly List<(int Bot, TerrainBlockKey Key, uint Version, long At)> _seenBlocks = [];
    private readonly HashSet<(int Bot, OpId Op)> _counted = [];
    private readonly List<double> _samples = [];

    /// <summary>Initializes a new instance of the <see cref="EditLatencyTracker"/> class.</summary>
    /// <param name="time">The clock of every timestamp.</param>
    /// <param name="horizon">How long unmatched events are kept; 30 s when null.</param>
    public EditLatencyTracker(TimeProvider time, TimeSpan? horizon = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _horizon = (long)((horizon ?? TimeSpan.FromSeconds(30)).TotalSeconds * time.TimestampFrequency);
    }

    /// <summary>Gets the samples in milliseconds, in the order they were matched.</summary>
    public IReadOnlyList<double> Samples => _samples;

    /// <summary>Subscribes to the events of a bot; call once per bot with distinct indices.</summary>
    /// <param name="bot">The bot.</param>
    /// <param name="index">Index of the bot in the crowd.</param>
    public void Attach(BotClient bot, int index)
    {
        ArgumentNullException.ThrowIfNull(bot);
        bot.Stats.Recorded += record =>
        {
            if (record.Reason == Horyzont.Core.Intents.RejectReason.None && record.AppliedOpId != OpId.None)
            {
                OnApplied(index, record.AppliedOpId, record.SentAt);
            }
        };
        var (constructs, terrain) = bot.Replica switch
        {
            BotReplicaSet set => (set.Constructs, set.Terrain),
            ConstructMirrorReplica c => (c, null),
            TerrainReplica t => (null, t),
            _ => ((ConstructMirrorReplica?)null, (TerrainReplica?)null),
        };
        if (constructs is not null)
        {
            constructs.OperationObserved += op => OnOperationSeen(index, op);
        }

        if (terrain is not null)
        {
            terrain.BarrierCompleted += (op, entries) => OnBarrier(op, entries);
            terrain.BlockStored += (key, version) => OnBlockSeen(index, key, version);
        }
    }

    /// <summary>The author learned that its intent was applied as an operation.</summary>
    /// <param name="bot">Author.</param>
    /// <param name="op">The operation.</param>
    /// <param name="sentAt">When the intent was submitted.</param>
    public void OnApplied(int bot, OpId op, long sentAt) => _sent[op] = (bot, sentAt, _time.GetTimestamp());

    /// <summary>The author learned the blocks and versions its terrain edit produced.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="entries">Blocks and versions.</param>
    public void OnBarrier(OpId op, IReadOnlyList<TerrainSyncEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var now = _time.GetTimestamp();
        foreach (var entry in entries)
        {
            _versions[(entry.Key, entry.Version)] = (op, now);
        }
    }

    /// <summary>A bot received an operation message.</summary>
    /// <param name="bot">The bot.</param>
    /// <param name="op">The operation.</param>
    public void OnOperationSeen(int bot, OpId op) => _seenOps.Add((bot, op, _time.GetTimestamp()));

    /// <summary>A bot stored a terrain block.</summary>
    /// <param name="bot">The bot.</param>
    /// <param name="key">Block.</param>
    /// <param name="version">Version.</param>
    public void OnBlockSeen(int bot, TerrainBlockKey key, uint version) => _seenBlocks.Add((bot, key, version, _time.GetTimestamp()));

    /// <summary>Pairs the events received so far and drops unmatched ones older than the horizon.</summary>
    public void Resolve()
    {
        var now = _time.GetTimestamp();
        _seenOps.RemoveAll(seen =>
        {
            if (_sent.TryGetValue(seen.Op, out var sent))
            {
                Count(sent.Bot, seen.Bot, seen.Op, sent.SentAt, seen.At);
                return true;
            }

            return now - seen.At > _horizon;
        });
        _seenBlocks.RemoveAll(seen =>
        {
            if (_versions.TryGetValue((seen.Key, seen.Version), out var version) && _sent.TryGetValue(version.Op, out var sent))
            {
                Count(sent.Bot, seen.Bot, version.Op, sent.SentAt, seen.At);
                return true;
            }

            return now - seen.At > _horizon;
        });
        foreach (var op in _sent.Where(p => now - p.Value.At > _horizon).Select(p => p.Key).ToList())
        {
            _sent.Remove(op);
        }

        foreach (var key in _versions.Where(p => now - p.Value.At > _horizon).Select(p => p.Key).ToList())
        {
            _versions.Remove(key);
        }

        if (_counted.Count > 100_000)
        {
            _counted.Clear();
        }
    }

    /// <summary>Summarises the samples.</summary>
    /// <returns>Count, p50, p99 and maximum in milliseconds (zeros without samples).</returns>
    public (int Count, double P50, double P99, double Max) Summary()
    {
        Resolve();
        if (_samples.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var sorted = _samples.ToArray();
        Array.Sort(sorted);
        return (sorted.Length, Percentile(sorted, 0.50), Percentile(sorted, 0.99), sorted[^1]);
    }

    private static double Percentile(double[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];

    private void Count(int author, int viewer, OpId op, long sentAt, long seenAt)
    {
        // The author's own view is prediction territory; one sample per (viewer, operation): the first block of a terrain edit counts.
        if (author == viewer || !_counted.Add((viewer, op)))
        {
            return;
        }

        _samples.Add((seenAt - sentAt) * 1000.0 / _time.TimestampFrequency);
    }
}
