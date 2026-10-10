using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Core.Tests.Frames;

public class FramePoseHistoryTests
{
    private static readonly FrameRef _ship = FrameRef.Of(new EntityId(7));
    private static readonly FrameRef _other = FrameRef.Of(new EntityId(8));

    [Fact]
    public void FramePoseHistory_InterpolatesBetweenTicks_And500msWindow()
    {
        var history = new FramePoseHistory();
        Assert.Equal(31, FramePoseHistory.HistoryTicks);
        Assert.Equal(64, history.Capacity);

        // 100 ticks of a frame moving 0.5 m per tick along X and turning 0.01 rad per tick about Y.
        for (ulong t = 1; t <= 100; t++)
        {
            Assert.True(history.Record(new Tick(t), _ship, PoseAt(t)));
        }

        // Exact ticks and halfway between them.
        Assert.True(history.TryGetWorldPose(_ship, 100, out var latest));
        Assert.Equal(PoseAt(100), latest);
        Assert.True(history.TryGetWorldPose(_ship, 85.5, out var half));
        Assert.Equal(42.75, half.Position.X, 1e-9);
        var expectedRotation = QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), 0.855);
        Assert.InRange(Math.Abs(QuaternionD.Dot(expectedRotation, half.Rotation)), 1 - 1e-12, 1 + 1e-12);

        // The window holds 31 ticks (500 ms at 60 Hz): 70 is the oldest, 69.9 and later than the newest are outside.
        Assert.True(history.TryGetWorldPose(_ship, 70, out var oldest));
        Assert.Equal(PoseAt(70), oldest);
        Assert.False(history.TryGetWorldPose(_ship, 69.9, out _));
        Assert.False(history.TryGetWorldPose(_ship, 100.1, out _));
        Assert.False(history.TryGetWorldPose(_other, 90, out _));

        // An older tick does not overwrite; the same tick replaces the newest sample.
        Assert.False(history.Record(new Tick(99), _ship, PoseD.Identity));
        Assert.True(history.Record(new Tick(100), _ship, PoseAt(200)));
        Assert.True(history.TryGetWorldPose(_ship, 100, out var replaced));
        Assert.Equal(PoseAt(200), replaced);

        // A gap: the samples on both sides of it still interpolate.
        Assert.True(history.Record(new Tick(110), _ship, PoseAt(110)));
        Assert.True(history.TryGetWorldPose(_ship, 105, out var gap));
        Assert.Equal((100.0 + 55.0) / 2, gap.Position.X, 1e-9);

        // Forget removes the frame.
        Assert.True(history.Forget(_ship));
        Assert.False(history.Forget(_ship));
        Assert.False(history.TryGetWorldPose(_ship, 110, out _));
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void FramePoseHistory_Capacity_RefusesNewFramesWhenFull()
    {
        var history = new FramePoseHistory(capacity: 2);

        Assert.True(history.Record(new Tick(1), _ship, PoseAt(1)));
        Assert.True(history.Record(new Tick(1), _other, PoseAt(1)));
        Assert.False(history.Record(new Tick(1), FrameRef.Of(new EntityId(9)), PoseAt(1)));
        Assert.True(history.Contains(_ship));

        // A forgotten slot is reused and starts empty.
        history.Forget(_ship);
        Assert.True(history.Record(new Tick(5), FrameRef.Of(new EntityId(9)), PoseAt(5)));
        Assert.False(history.TryGetWorldPose(FrameRef.Of(new EntityId(9)), 1, out _));
        Assert.True(history.TryGetWorldPose(FrameRef.Of(new EntityId(9)), 5, out _));
    }

    [Fact]
    public void FramePoseHistory_SteadyState_AllocatesZeroBytes()
    {
        var history = new FramePoseHistory();
        var frames = new FrameRef[64];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = FrameRef.Of(new EntityId((ulong)(i + 1)));
        }

        ulong tick = 1;
        for (; tick <= 40; tick++)
        {
            Record(history, frames, tick);
        }

        // Forget and re-add a frame once during warm-up, then 10 000 records and lookups.
        history.Forget(frames[3]);
        Record(history, frames, tick++);
        var sink = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000 / frames.Length; i++, tick++)
        {
            Record(history, frames, tick);
            for (var f = 0; f < frames.Length; f++)
            {
                history.TryGetWorldPose(frames[f], tick - 10.5, out var pose);
                sink += pose.Position.X;
            }

            history.Forget(frames[i % frames.Length]);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(double.IsFinite(sink));

        static void Record(FramePoseHistory history, FrameRef[] frames, ulong tick)
        {
            for (var f = 0; f < frames.Length; f++)
            {
                history.Record(new Tick(tick), frames[f], PoseAt(tick + (ulong)f));
            }
        }
    }

    private static PoseD PoseAt(ulong tick) =>
        new(new Vector3D(tick * 0.5, 1, -2), QuaternionD.FromAxisAngle(new Vector3D(0, 1, 0), tick * 0.01));
}
