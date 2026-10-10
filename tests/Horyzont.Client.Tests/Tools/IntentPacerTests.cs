using Horyzont.Client.Tools;
using Horyzont.Core.Simulation;

namespace Horyzont.Client.Tests.Tools;

public sealed class IntentPacerTests
{
    private static void AssertEveryWindowAtMost(List<int> ticks, int length, int total, int max)
    {
        for (var start = 0; start + 60 <= length; start++)
        {
            Assert.InRange(ticks.Count(t => t >= start && t < start + 60), 0, max);
        }

        Assert.InRange(ticks.Count, 1, total);
    }

    [Fact]
    public void IntentPacer_HoldDigTenSeconds_AtMostFiveInEveryOneSecondWindow()
    {
        var pacer = new IntentPacer(new GameRules());
        var held = new List<int>();
        var mashed = new List<int>();
        var mixed = new List<int>();
        var pacerMashed = new IntentPacer(new GameRules());
        var pacerMixed = new IntentPacer(new GameRules());
        for (var tick = 0; tick < 600; tick++)
        {
            if (pacer.TryAcquire(PaceClass.Terrain, repeat: tick != 0, tick))
            {
                held.Add(tick);
            }

            if (pacerMashed.TryAcquire(PaceClass.Terrain, repeat: false, tick))
            {
                mashed.Add(tick);
            }

            if (pacerMixed.TryAcquire(PaceClass.Terrain, repeat: tick % 7 != 0, tick))
            {
                mixed.Add(tick);
            }
        }

        AssertEveryWindowAtMost(held, 600, 50, 5);
        AssertEveryWindowAtMost(mashed, 600, 50, 5);
        AssertEveryWindowAtMost(mixed, 600, 50, 5);
        Assert.True(held.Count >= 40, "A held button must keep digging at about 4.6 per second.");
    }

    [Fact]
    public void IntentPacer_HeldTerrainAction_RepeatsEveryThirteenTicks()
    {
        var pacer = new IntentPacer(new GameRules());
        var sent = new List<int>();
        for (var tick = 0; tick < 100; tick++)
        {
            if (pacer.TryAcquire(PaceClass.Terrain, repeat: true, tick))
            {
                sent.Add(tick);
            }
        }

        Assert.Equal([0, 13, 26, 39, 52, 65, 78, 91], sent);
    }

    [Fact]
    public void IntentPacer_HeldBlockAction_RepeatsEveryFifteenTicks()
    {
        var pacer = new IntentPacer(new GameRules());
        var sent = new List<int>();
        for (var tick = 0; tick < 50; tick++)
        {
            if (pacer.TryAcquire(PaceClass.Block, repeat: true, tick))
            {
                sent.Add(tick);
            }
        }

        Assert.Equal([0, 15, 30, 45], sent);
    }

    [Fact]
    public void IntentPacer_BlockClicks_TenBurstThenTenPerSecond()
    {
        var pacer = new IntentPacer(new GameRules());
        var burst = 0;
        for (var i = 0; i < 20; i++)
        {
            burst += pacer.TryAcquire(PaceClass.Block, repeat: false, 0) ? 1 : 0;
        }

        Assert.Equal(10, burst);
        Assert.False(pacer.TryAcquire(PaceClass.Block, repeat: false, 5));
        Assert.True(pacer.TryAcquire(PaceClass.Block, repeat: false, 6));
    }

    [Fact]
    public void IntentPacer_ClassesAreIndependent_AndRefusalCostsNothing()
    {
        var pacer = new IntentPacer(new GameRules());
        for (var i = 0; i < 5; i++)
        {
            Assert.True(pacer.TryAcquire(PaceClass.Terrain, repeat: false, 0));
        }

        Assert.False(pacer.TryAcquire(PaceClass.Terrain, repeat: false, 0));
        Assert.False(pacer.TryAcquire(PaceClass.Terrain, repeat: false, 30));
        Assert.True(pacer.TryAcquire(PaceClass.Block, repeat: false, 30));
        Assert.True(pacer.TryAcquire(PaceClass.Terrain, repeat: false, 60));
    }

    [Fact]
    public void IntentPacer_LongPause_RefillsAtMostToTheBurst()
    {
        var pacer = new IntentPacer(new GameRules());
        for (var i = 0; i < 5; i++)
        {
            Assert.True(pacer.TryAcquire(PaceClass.Terrain, repeat: false, 0));
        }

        var accepted = 0;
        for (var i = 0; i < 10; i++)
        {
            accepted += pacer.TryAcquire(PaceClass.Terrain, repeat: false, 1_000_000) ? 1 : 0;
        }

        Assert.Equal(5, accepted);
    }
}
