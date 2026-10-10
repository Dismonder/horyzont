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
