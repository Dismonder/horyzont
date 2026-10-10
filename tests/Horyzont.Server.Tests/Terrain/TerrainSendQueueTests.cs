using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;
using Horyzont.Server.Replication;
using Horyzont.Server.Terrain;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Terrain;

public sealed class TerrainSendQueueTests
{
    private static readonly PlayerId _player = new(1);
    private static readonly TerrainBlockKey _key = new(new PlanetId(1), 0, -1, 2, 3);

    [Fact]
    public void Queue_RepeatedEdits_SendsLatestVersion()
    {
        var time = new FakeTimeProvider();
        var queue = new TerrainSendQueue(new TokenBucketReplicationBudget(time), time.TimestampFrequency);
        queue.Enqueue(_player, _key, 1, [1], 0, 1);
        queue.Enqueue(_player, _key, 3, [3], 0, 1);
        queue.Enqueue(_player, _key, 2, [2], 0, 1);
        Assert.True(queue.TryTake(_player, time.GetTimestamp(), out var data));
        Assert.Equal(3u, data!.Version);
        Assert.Equal(new byte[] { 3 }, data.Data);
        Assert.False(queue.TryTake(_player, time.GetTimestamp(), out _));
    }

    [Fact]
    public void Queue_Bulk_DebitsActualFrameAndSharesLiveDebt()
    {
        var time = new FakeTimeProvider();
        var budget = new TokenBucketReplicationBudget(time);
        var queue = new TerrainSendQueue(budget, time.TimestampFrequency);
        queue.Enqueue(_player, _key, 1, new byte[100], 1, 1);
        var w = new ByteWriter();
        MessageCodec.Write(w, new TerrainBlockData(_key, 1, new byte[100]));
        Assert.True(budget.TryConsume(_player, ReplicationTraffic.Live, 65536 - w.Length + 1));
        Assert.False(queue.TryTake(_player, time.GetTimestamp(), out _));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(queue.TryTake(_player, time.GetTimestamp(), out _));
    }

    [Fact]
    public void Queue_AgedBulkBlock_DoesNotOvertakeLive()
    {
        var time = new FakeTimeProvider();
        var budget = new TokenBucketReplicationBudget(time);
        var queue = new TerrainSendQueue(budget, time.TimestampFrequency);
        var bulk = _key with { X = 10 };
        var live = _key with { X = 20 };
        queue.Enqueue(_player, bulk, 1, [1], 1, 1);
        Assert.True(budget.TryConsume(_player, ReplicationTraffic.Live, 65536));
        var later = time.TimestampFrequency * 5;
        Assert.False(queue.TryTake(_player, later, out _));
        queue.Enqueue(_player, live, 1, [1], 0, 9000);
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.True(queue.TryTake(_player, later, out var data));
        Assert.Equal(live, data!.Key);
    }

    [Fact]
    public void Queue_NewerBulkVersionOfALiveBlock_KeepsLivePriority()
    {
        var time = new FakeTimeProvider();
        var queue = new TerrainSendQueue(new TokenBucketReplicationBudget(time), time.TimestampFrequency);
        queue.Enqueue(_player, _key, 1, [1], 0, 9000);
        queue.Enqueue(_player, _key with { X = 10 }, 1, [1], 1, 1);
        queue.Enqueue(_player, _key, 2, [2], 1, 9000);

        Assert.True(queue.TryTake(_player, time.GetTimestamp(), out var data));
        Assert.Equal(_key.X, data!.Key.X);
        Assert.Equal(2u, data.Version);
    }

    [Fact]
    public void Queue_LiveOnly_SkipsBulkAndTakesLive()
    {
        var time = new FakeTimeProvider();
        var queue = new TerrainSendQueue(new TokenBucketReplicationBudget(time), time.TimestampFrequency);
        queue.Enqueue(_player, _key with { X = 10 }, 1, [1], 1, 1);
        Assert.False(queue.TryTake(_player, time.GetTimestamp(), liveOnly: true, out _));
        queue.Enqueue(_player, _key with { X = 20 }, 1, [1], 0, 9000);

        Assert.True(queue.TryTake(_player, time.GetTimestamp(), liveOnly: true, out var data));
        Assert.Equal(20, data!.Key.X);
        Assert.False(queue.TryTake(_player, time.GetTimestamp(), liveOnly: true, out _));
        Assert.Equal(1, queue.Count(_player));
    }

    [Fact]
    public void Queue_SustainedLive_StillSendsABulkBlockEveryFewTakes()
    {
        var time = new FakeTimeProvider();
        var queue = new TerrainSendQueue(new TokenBucketReplicationBudget(time), time.TimestampFrequency);
        queue.Enqueue(_player, _key with { X = 1000 }, 1, [1], 1, 1);
        for (var i = 0; i < 40; i++)
        {
            queue.Enqueue(_player, _key with { X = i }, 1, [1], 0, i);
        }

        var order = new List<int>();
        while (queue.TryTake(_player, time.GetTimestamp(), out var data))
        {
            order.Add(data!.Key.X);
        }

        var bulkAt = order.IndexOf(1000);
        Assert.InRange(bulkAt, 0, TerrainSendQueue.LiveTakesPerBulk);
        Assert.Equal(41, order.Count);
    }

    [Fact]
    public void Queue_SustainedLiveWhileOnlyLiveIsWanted_NeverSendsBulk()
    {
        var time = new FakeTimeProvider();
        var queue = new TerrainSendQueue(new TokenBucketReplicationBudget(time), time.TimestampFrequency);
        queue.Enqueue(_player, _key with { X = 1000 }, 1, [1], 1, 1);
        for (var i = 0; i < 20; i++)
        {
            queue.Enqueue(_player, _key with { X = i }, 1, [1], 0, i);
        }

        while (queue.TryTake(_player, time.GetTimestamp(), liveOnly: true, out var data))
        {
            Assert.NotEqual(1000, data!.Key.X);
        }

        Assert.Equal(1, queue.Count(_player));
    }

    [Fact]
    public void Queue_DistantFlood_BoundedAndMarksResync()
    {
        var time = new FakeTimeProvider();
        var queue = new TerrainSendQueue(new TokenBucketReplicationBudget(time), time.TimestampFrequency);
        for (var i = 0; i < 4097; i++)
        {
            queue.Enqueue(_player, _key with { X = i }, 1, [1], 1, i);
        }
        Assert.True(queue.NeedsResync(_player));
        Assert.Equal(4096, queue.Count(_player));
        queue.Remove(_player);
        Assert.False(queue.NeedsResync(_player));
        Assert.Equal(0, queue.Count(_player));
    }

    [Fact]
    public void OnTick_IdleAndWarmedSelection_AllocatesZeroBytes()
    {
        var time = new FakeTimeProvider();
        var queue = new TerrainSendQueue(new TokenBucketReplicationBudget(time), time.TimestampFrequency);
        queue.TryTake(_player, 0, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            queue.TryTake(_player, 0, out _);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        for (var i = 0; i < 100; i++)
        {
            queue.Enqueue(_player, _key with { X = i }, 1, [1], 0, i);
        }
        queue.TryTake(_player, 0, out _);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 99; i++)
        {
            queue.TryTake(_player, 0, out _);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
