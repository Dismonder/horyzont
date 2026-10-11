using Horyzont.Server.Diagnostics;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Diagnostics;

public sealed class TickTimeRecorderTests
{
    [Fact]
    public void LastMilliseconds_IsTheLatestTick_AndSurvivesTheWindow()
    {
        var time = new FakeTimeProvider();
        var recorder = new TickTimeRecorder(time);
        Assert.True(double.IsNaN(recorder.LastMilliseconds));

        Measure(recorder, time, 3.0);
        Measure(recorder, time, 5.5);
        _ = recorder.TakeWindow();

        Assert.Equal(5.5, recorder.LastMilliseconds, 3);
    }

    [Fact]
    public void Record_Window_ReportsMeanP95P99Max()
    {
        var time = new FakeTimeProvider();
        var recorder = new TickTimeRecorder(time);

        // 98 ticks of 2 ms, one of 10 ms and one of 40 ms.
        for (var i = 0; i < 98; i++)
        {
            Measure(recorder, time, 2.0);
        }

        Measure(recorder, time, 10.0);
        Measure(recorder, time, 40.0);

        var window = recorder.TakeWindow();

        Assert.Equal(100, window.Count);
        Assert.Equal((98 * 2.0 + 10.0 + 40.0) / 100.0, window.MeanMs, 3);
        Assert.Equal(40.0, window.MaxMs, 3);
        Assert.InRange(window.P95Ms, 2.0, 2.1);
        Assert.InRange(window.P99Ms, 10.0, 10.1);
    }

    [Fact]
    public void TakeWindow_ResetsTheWindow()
    {
        var time = new FakeTimeProvider();
        var recorder = new TickTimeRecorder(time);
        Measure(recorder, time, 5.0);
        Assert.Equal(1, recorder.TakeWindow().Count);

        var empty = recorder.TakeWindow();

        Assert.Equal(0, empty.Count);
        Assert.Equal(0.0, empty.MeanMs);
        Assert.Equal(0.0, empty.MaxMs);
        Assert.Equal(0.0, empty.P99Ms);
    }

    [Fact]
    public void Record_VeryLongTick_IsCountedInTheLastBucketAndKeepsExactMax()
    {
        var time = new FakeTimeProvider();
        var recorder = new TickTimeRecorder(time);

        Measure(recorder, time, 5000.0);

        var window = recorder.TakeWindow();
        Assert.Equal(1, window.Count);
        Assert.Equal(5000.0, window.MaxMs, 3);
        Assert.Equal(5000.0, window.P99Ms, 3);
    }

    [Fact]
    public void Record_SteadyState_AllocatesZeroBytes()
    {
        var time = new FakeTimeProvider();
        var recorder = new TickTimeRecorder(time);
        for (var i = 0; i < 1000; i++)
        {
            Measure(recorder, time, 3.0 + (i % 7));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            var start = recorder.Begin();
            time.Advance(TimeSpan.FromMilliseconds(3 + (i % 7)));
            recorder.End(start);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(recorder.TakeWindow().Count > 0);
    }

    private static void Measure(TickTimeRecorder recorder, FakeTimeProvider time, double milliseconds)
    {
        var start = recorder.Begin();
        time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        recorder.End(start);
    }
}
