using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>Per-client actual-byte snapshot accounting, capped at 20,480 B/s and a 2,300 B burst.</summary>
public sealed class MotionSnapshotBudget : ISnapshotBudget
{
    private const int Capacity = 2300;
    private const int BytesPerSecond = 20480;
    private readonly TimeProvider _time;
    private readonly Dictionary<PlayerId, Bucket> _buckets = [];

    /// <summary>Creates a snapshot budget using monotonic session time.</summary>
    /// <param name="time">Clock for token refill.</param>
    public MotionSnapshotBudget(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <inheritdoc />
    public int GetByteBudget(PlayerId player, Tick tick) => GetByteBudget(player, tick, false);

    /// <summary>Gets a bounded frame allowance; forced full frames share the same bucket.</summary>
    /// <param name="player">Receiving player.</param>
    /// <param name="tick">Simulation tick; refill uses real monotonic time.</param>
    /// <param name="forcedFull">Whether a required full frame can use the 1,150 B limit.</param>
    /// <returns>Floor of available tokens, capped by the frame limit.</returns>
    public int GetByteBudget(PlayerId player, Tick tick, bool forcedFull)
    {
        var bucket = Refill(player);
        return (int)Math.Min(forcedFull ? 1150 : 1024, Math.Max(0, bucket.Tokens));
    }

    /// <inheritdoc />
    public void Debit(PlayerId player, int encodedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(encodedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(encodedBytes, 1150);
        var bucket = Refill(player);
        if (encodedBytes > bucket.Tokens)
        {
            throw new InvalidOperationException("Snapshot builder exceeded the available byte budget.");
        }

        _buckets[player] = bucket with { Tokens = bucket.Tokens - encodedBytes };
    }

    /// <inheritdoc />
    public void Forget(PlayerId player) => Remove(player);

    /// <summary>Forgets a player's bucket after the session ends.</summary>
    /// <param name="player">Disconnected player.</param>
    public void Remove(PlayerId player) => _buckets.Remove(player);

    private Bucket Refill(PlayerId player)
    {
        var now = _time.GetTimestamp();
        if (!_buckets.TryGetValue(player, out var bucket))
        {
            bucket = new Bucket(Capacity, now);
        }

        bucket = new Bucket(Math.Min(Capacity, bucket.Tokens + _time.GetElapsedTime(bucket.Timestamp, now).TotalSeconds * BytesPerSecond), now);
        _buckets[player] = bucket;
        return bucket;
    }

    private readonly record struct Bucket(double Tokens, long Timestamp);
}
