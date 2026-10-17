using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Movement;

public sealed class RemoteMotionBufferTests
{
    private static RemoteMotionSample Sample(ulong tick, float x = 0) => new(new EntityId(2), FrameRef.Of(new PlanetId(1)), 0, new Tick(tick), new Vector3(x, 0, 0), Quaternion.Identity, new Vector3(6, 0, 0), (byte)MovementMode.Walking);

    [Fact]
    public void TrySample_TwoSamples_InterpolatesAtSixTickDelay()
    {
        var buffer = new RemoteMotionBuffer();
        Assert.True(buffer.Push(Sample(100)));
        Assert.True(buffer.Push(Sample(103, 0.3f) with { Rotation = -Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f) }));
        Assert.True(buffer.TrySample(107 - RemoteMotionPolicy.InterpolationTicks, out var result));
        Assert.Equal(0.1f, result.Position.X, 6);
        Assert.True(MathF.Abs(Quaternion.Dot(result.Rotation, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f))) > 0.99999f);
    }

    [Fact]
    public void TrySample_PacketGap_ExtrapolatesForThreeTicksThenHolds()
    {
        var buffer = new RemoteMotionBuffer();
        buffer.Push(Sample(100));
        Assert.True(buffer.TrySample(103, out var atLimit));
        Assert.True(buffer.TrySample(115, out var held));
        Assert.Equal(0.3f, atLimit.Position.X, 6);
        Assert.Equal(atLimit.Position, held.Position);
        Assert.Equal(1, buffer.SampleUnderruns);
    }

    [Fact]
    public void Push_StaleTickOrParentChange_DropsOrResetsSamples()
    {
        var buffer = new RemoteMotionBuffer();
        buffer.Push(Sample(100));
        Assert.False(buffer.Push(Sample(100)));
        Assert.False(buffer.Push(Sample(99)));
        buffer.Push(Sample(103) with { ParentSeq = 1 });
        Assert.Equal(1, buffer.Count);
        Assert.False(buffer.Push(Sample(104) with { ParentSeq = 0 }));
        buffer.Push(Sample(106, 4) with { ParentSeq = 1 });
        Assert.Equal(1, buffer.Count);
        buffer.Clear();
        Assert.Equal(0, buffer.Count);
        Assert.False(buffer.TrySample(100, out _));
        Assert.True(buffer.Push(Sample(1)));
    }

    [Fact]
    public void Push_DevFlightWithinModeSpeed_DoesNotTreatFastFlightAsTeleport()
    {
        var buffer = new RemoteMotionBuffer();
        buffer.Push(Sample(100) with { MotionFlags = (byte)MovementMode.DevFlying });
        buffer.Push(Sample(103, 7.5f) with { MotionFlags = (byte)MovementMode.DevFlying });
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void AdvanceClock_JitterAndDelayedSnapshot_NeverMovesRenderTimeBackwards()
    {
        var time = new FakeTimeProvider();
        var policy = new RemoteMotionPolicy(time);
        policy.Start(new Tick(100), 60);
        var previous = policy.RenderTick;
        var buffer = new RemoteMotionBuffer();
        for (ulong tick = 101; tick < 180; tick++)
        {
            time.Advance(TimeSpan.FromMilliseconds(tick % 2 == 0 ? 10 : 23));
            Assert.False(policy.Observe(new Tick(tick - 3)));
            Assert.True(policy.RenderTick >= previous);
            previous = policy.RenderTick;
            buffer.Push(Sample(tick));
        }
        Assert.Equal(32, buffer.Count);
        Assert.True(policy.Observe(new Tick(1000)));
        policy.Reset();
        Assert.Equal(0, policy.RenderTick);
    }

    [Fact]
    public void AdvanceAndSample_WarmedUp_AllocatesZeroBytes()
    {
        var correction = new PresentationCorrection();
        var buffer = new RemoteMotionBuffer();
        buffer.Push(Sample(100));
        buffer.Push(Sample(103, 0.3f));
        for (var i = 0; i < 10000; i++) { correction.Advance(0.001f); buffer.TrySample(101.5, out _); }
        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) { correction.Advance(0.001f); buffer.TrySample(101.5, out _); }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - start);
        }

        Assert.Equal(0, smallest);
    }
}
