using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Terrain;

namespace Horyzont.Persistence.Tests.Terrain;

public sealed class DerivedRebuildScheduleTests
{
    private static TerrainBlockKey Key(byte lod, int x) => new(new PlanetId(1), lod, x, 0, 0);

    [Fact]
    public void NextAttempts_BlockedLowBlocks_DoNotStarveAHigherBlock()
    {
        var queue = new DerivedRebuildSchedule(100);
        for (var i = 0; i < 40; i++)
        {
            queue.MarkDirty(Key(1, i));
        }

        var buildable = Key(2, 0);
        queue.MarkDirty(buildable);
        var attempts = new List<TerrainBlockKey>();
        var built = false;
        for (var step = 0; step < 10 && !built; step++)
        {
            queue.NextAttempts(attempts, 16);
            foreach (var key in attempts)
            {
                queue.Attempted(key);
                if (key == buildable)
                {
                    queue.Publish(key, [1]);
                    built = true;
                }
            }
        }

        Assert.True(built);
        Assert.False(queue.IsDirty(buildable));
    }

    [Fact]
    public void NextAttempts_AfterTheLastDirtyBlock_WrapsAround()
    {
        var queue = new DerivedRebuildSchedule(100);
        for (var i = 0; i < 3; i++)
        {
            queue.MarkDirty(Key(1, i));
        }

        var attempts = new List<TerrainBlockKey>();
        queue.Attempted(Key(1, 2));
        queue.NextAttempts(attempts, 16);

        Assert.Equal([Key(1, 0), Key(1, 1), Key(1, 2)], attempts);
    }

    [Fact]
    public void NextAttempts_RespectsTheLimit_AndStartsAfterTheCursor()
    {
        var queue = new DerivedRebuildSchedule(100);
        for (var i = 0; i < 5; i++)
        {
            queue.MarkDirty(Key(1, i));
        }

        var attempts = new List<TerrainBlockKey>();
        queue.Attempted(Key(1, 1));
        queue.NextAttempts(attempts, 2);

        Assert.Equal([Key(1, 2), Key(1, 3)], attempts);
    }

    [Fact]
    public void Publish_AtCapacity_EvictsTheOldestOtherResultBackToDirty()
    {
        var queue = new DerivedRebuildSchedule(2);
        queue.Publish(Key(1, 0), [0]);
        queue.Publish(Key(1, 1), [1]);
        queue.Publish(Key(1, 2), [2]);

        Assert.True(queue.IsDirty(Key(1, 0)));
        Assert.False(queue.TryGetReady(Key(1, 0), out _));
        Assert.True(queue.TryGetReady(Key(1, 1), out _));
        Assert.True(queue.TryGetReady(Key(1, 2), out _));
        Assert.Equal(2, queue.ReadyCount);
    }

    [Fact]
    public void Publish_AnAlreadyReadyKeyAtCapacity_EvictsNothing()
    {
        var queue = new DerivedRebuildSchedule(2);
        queue.Publish(Key(1, 0), [0]);
        queue.Publish(Key(1, 1), [1]);
        queue.MarkDirty(Key(1, 1));
        queue.Publish(Key(1, 1), [9]);

        Assert.Equal(0, queue.DirtyCount);
        Assert.True(queue.TryGetReady(Key(1, 0), out _));
        Assert.True(queue.TryGetReady(Key(1, 1), out var blob));
        Assert.Equal([9], blob);
    }

    [Fact]
    public void Publish_ManyRounds_KeepsTheBoundsAndNeverEvictsTheJustPublishedBlock()
    {
        var queue = new DerivedRebuildSchedule(4);
        for (var i = 0; i < 200; i++)
        {
            queue.Publish(Key(1, i), [(byte)i]);
            Assert.True(queue.TryGetReady(Key(1, i), out _));
            Assert.True(queue.ReadyCount <= 4);
            if (i % 3 == 0)
            {
                queue.MarkDirty(Key(1, i));
            }
        }
    }

    [Fact]
    public void TakeReloadDue_NothingMissed_IsNeverDue()
    {
        var queue = new DerivedRebuildSchedule(10);
        queue.MarkDirty(Key(1, 0));
        queue.Publish(Key(1, 0), [1]);

        Assert.False(queue.TakeReloadDue(TimeSpan.Zero));
    }

    [Fact]
    public void TakeReloadDue_MissedBlockRebuiltWhileOthersStayDirty_IsDueWithoutWaitingForThem()
    {
        var queue = new DerivedRebuildSchedule(10);
        queue.MarkDirty(Key(1, 0));
        queue.MarkDirty(Key(2, 0));
        queue.NoteMissed(Key(1, 0));
        Assert.False(queue.TakeReloadDue(TimeSpan.Zero));

        queue.Publish(Key(1, 0), [1]);
        Assert.True(queue.TakeReloadDue(TimeSpan.Zero));
        Assert.False(queue.TakeReloadDue(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void TakeReloadDue_MissedBlockStillDirty_StaysRememberedUntilItIsRebuilt()
    {
        var queue = new DerivedRebuildSchedule(10);
        queue.MarkDirty(Key(1, 0));
        queue.MarkDirty(Key(1, 1));
        queue.NoteMissed(Key(1, 0));
        queue.NoteMissed(Key(1, 1));
        queue.Publish(Key(1, 0), [1]);
        Assert.True(queue.TakeReloadDue(TimeSpan.Zero));

        queue.Publish(Key(1, 1), [2]);
        Assert.False(queue.TakeReloadDue(TimeSpan.FromSeconds(1)));
        Assert.True(queue.TakeReloadDue(DerivedRebuildSchedule.MinReloadInterval));
        Assert.False(queue.TakeReloadDue(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void TakeReloadDue_ManyMissedBlocks_KeepsAMemoryBoundAndStillReloads()
    {
        var queue = new DerivedRebuildSchedule(10);
        for (var i = 0; i < DerivedRebuildSchedule.MaxMissedKeys + 50; i++)
        {
            queue.MarkDirty(Key(1, i));
            queue.NoteMissed(Key(1, i));
        }

        for (var i = 0; i < DerivedRebuildSchedule.MaxMissedKeys + 50; i++)
        {
            queue.Publish(Key(1, i), [1], "fp");
        }

        Assert.True(queue.TakeReloadDue(TimeSpan.Zero));
        Assert.False(queue.TakeReloadDue(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Publish_MoreChangedBlocksThanTheCapacity_ConvergesAndKeepsEveryBlockCurrent()
    {
        var queue = new DerivedRebuildSchedule(8);
        const int total = 100;
        for (var i = 0; i < total; i++)
        {
            queue.MarkDirty(Key(1, i));
        }

        var attempts = new List<TerrainBlockKey>();
        for (var step = 0; step < total && queue.DirtyCount > 0; step++)
        {
            queue.NextAttempts(attempts, 4);
            foreach (var key in attempts)
            {
                queue.Publish(key, [1], $"fp{key.X}");
                queue.Attempted(key);
            }
        }

        Assert.Equal(0, queue.DirtyCount);
        Assert.True(queue.ReadyCount <= 8);
        Assert.Equal(total - queue.ReadyCount, queue.EvictedCount);
        for (var i = 0; i < total; i++)
        {
            Assert.True(queue.Tracks(Key(1, i)));
            Assert.False(queue.IsDirty(Key(1, i)));
        }

        Assert.True(queue.TryGetEvicted(Key(1, 0), out var fingerprint));
        Assert.Equal("fp0", fingerprint);
        queue.MarkDirty(Key(1, 0));
        Assert.False(queue.TryGetEvicted(Key(1, 0), out _));
    }

    [Fact]
    public void Publish_EvictedBlockWithoutAFingerprint_GoesBackToDirty()
    {
        var queue = new DerivedRebuildSchedule(1);
        queue.Publish(Key(1, 0), [1]);
        queue.Publish(Key(1, 1), [2]);

        Assert.True(queue.IsDirty(Key(1, 0)));
        Assert.Equal(0, queue.EvictedCount);
    }
}
