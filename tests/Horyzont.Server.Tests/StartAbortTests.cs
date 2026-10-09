using System.Diagnostics;

using Horyzont.Persistence.Faults;
using Horyzont.Server.Tests.Support;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Tests;

[Collection(SqlitePools.Name)]
public sealed class StartAbortTests
{
    private static readonly TimeSpan _tick = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task StalledWriterInStartupCheckpoint_StartFailsAfterItsDeadline_CleanupAfterItsOwn()
    {
        using var release = new ManualResetEventSlim();
        var faults = new TestFaults();
        faults.PauseAt(FaultPoint.BeforeCheckpointCommit, release);
        await using var rig = new ServerRig(
            faults: faults,
            configure: o => o with { StartupCheckpointTimeout = TimeSpan.FromSeconds(2), StartCleanupTimeout = TimeSpan.FromSeconds(3) });
        try
        {
            var started = rig.Time.GetUtcNow();
            var start = rig.StartAsync();
            Assert.True(faults.Paused.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "the writer never reached the stall");

            var result = await DriveAsync(rig, start);

            var elapsed = rig.Time.GetUtcNow() - started;
            var failed = Assert.IsType<WorldStartResult.StartFailed>(result);
            Assert.Contains("did not finish in time", failed.Message, StringComparison.Ordinal);
            Assert.InRange(elapsed, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6));
            Assert.True(rig.Log.Contains(LogLevel.Warning, "start cleanup: dispose writer did not finish"));
            Assert.Equal(ServerRuntimeState.Stopped, rig.Runtime.RuntimeState);
            Assert.Equal(70, rig.Runtime.ExitCode);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task StopDuringStart_AbortsTheStartAndFinishesBounded()
    {
        using var release = new ManualResetEventSlim();
        var faults = new TestFaults();
        faults.PauseAt(FaultPoint.BeforeCheckpointCommit, release);

        // The startup checkpoint deadline is an hour: only the stop request can end the wait.
        await using var rig = new ServerRig(
            faults: faults,
            configure: o => o with { StartupCheckpointTimeout = TimeSpan.FromHours(1), StartCleanupTimeout = TimeSpan.FromSeconds(3) });
        try
        {
            var started = rig.Time.GetUtcNow();
            var start = rig.StartAsync();
            Assert.True(faults.Paused.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "the writer never reached the stall");
            Assert.Equal(ServerRuntimeState.Starting, rig.Runtime.RuntimeState);

            rig.Runtime.RequestShutdown();
            var result = await DriveAsync(rig, start);

            var elapsed = rig.Time.GetUtcNow() - started;
            Assert.IsType<WorldStartResult.Aborted>(result);
            Assert.False(result.IsRunning);
            Assert.InRange(elapsed, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4));
            Assert.Equal(ServerRuntimeState.Stopped, rig.Runtime.RuntimeState);
            Assert.True(rig.Runtime.IsStopped);
            Assert.Equal(0, rig.Runtime.ExitCode);
            Assert.True(rig.Log.Contains(LogLevel.Warning, "start cleanup: dispose writer did not finish"));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task StopDuringStart_ThenStartOnTheSameWorld_Works()
    {
        using var release = new ManualResetEventSlim();
        var faults = new TestFaults();
        faults.PauseAt(FaultPoint.BeforeCheckpointCommit, release);
        await using var first = new ServerRig(
            faults: faults,
            configure: o => o with { StartupCheckpointTimeout = TimeSpan.FromHours(1), StartCleanupTimeout = TimeSpan.FromSeconds(3) });
        var start = first.StartAsync();
        Assert.True(faults.Paused.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        first.Runtime.RequestShutdown();
        release.Set();

        Assert.IsType<WorldStartResult.Aborted>(await DriveAsync(first, start));

        await using var again = new ServerRig(first.Dir);
        Assert.IsType<WorldStartResult.Started>(await again.StartAsync());
        await again.StopAsync();
    }

    private static async Task<WorldStartResult> DriveAsync(ServerRig rig, Task<WorldStartResult> start)
    {
        var watch = Stopwatch.StartNew();
        while (!start.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            rig.Time.Advance(_tick);
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        Assert.True(start.IsCompleted, "the start did not finish");
        return await start;
    }
}
