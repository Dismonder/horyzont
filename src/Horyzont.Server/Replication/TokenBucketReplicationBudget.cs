using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>One reliable replication bucket per player, shared by entity and terrain senders.</summary>
public sealed class TokenBucketReplicationBudget : IReplicationSendBudget
{
    private const int Capacity = 65536;
    private const int BytesPerSecond = 27000;
    private readonly TimeProvider _time;
    private readonly Dictionary<PlayerId, Bucket> _buckets = [];

    /// <summary>Initializes a budget with a monotonic clock.</summary>
    /// <param name="time">Clock for refill; no simulation time is used.</param>
    public TokenBucketReplicationBudget(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <inheritdoc />
    public bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (traffic is not (ReplicationTraffic.Live or ReplicationTraffic.Bulk))
        {
            throw new ArgumentOutOfRangeException(nameof(traffic));
        }

        var now = _time.GetTimestamp();
        if (!_buckets.TryGetValue(player, out var bucket))
        {
            bucket = new Bucket(Capacity, now);
        }

        var tokens = Math.Min(Capacity, bucket.Tokens + (_time.GetElapsedTime(bucket.Timestamp, now).TotalSeconds * BytesPerSecond));
        var allowed = traffic == ReplicationTraffic.Live || tokens >= Math.Min(bytes, Capacity);
        _buckets[player] = new Bucket(allowed ? tokens - bytes : tokens, now);
        return allowed;
    }

    /// <inheritdoc />
    public void Forget(PlayerId player) => _buckets.Remove(player);

    private readonly record struct Bucket(double Tokens, long Timestamp);
}
