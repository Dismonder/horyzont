using Horyzont.Client.Movement;
using Horyzont.Core.Ids;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Movement;

public sealed class RemoteMotionPolicyTests
{
    [Fact]
    public void Observe_AfterLongIdle_SlewsAtMostOneTick()
    {
        var time = new FakeTimeProvider();
        var clock = new RemoteMotionPolicy(time);
        clock.Start(new Tick(100), 60);
        time.Advance(TimeSpan.FromSeconds(30));
        var before = clock.RenderTick;
        Assert.False(clock.Observe(new Tick(100 + 30 * 60 + 20)));
        Assert.True(clock.RenderTick - before <= 1.0 + 1e-6, $"render moved {clock.RenderTick - before}");
    }

    [Fact]
    public void DelaySeconds_ReportsCurrentDelayNotConstant()
    {
        var time = new FakeTimeProvider();
        var clock = new RemoteMotionPolicy(time);
        clock.Start(new Tick(100), 60);
        Assert.Equal(0.1, clock.DelaySeconds, 3);
    }
}
