using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Persistence.Faults;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests;

[Collection(SqlitePools.Name)]
public sealed class ShutdownSequenceTests
{
    private static readonly string[] _fullOrder =
    [
        "1 session shutdown",
        "2 drain session and transport",
        "3a participants shutdown",
        "3b leave all online players",
        "4 checkpoint",
        "5 flush journal",
        "6 dispose writer",
        "7 shut terrain down",
        "8 dispose transport",
    ];

    [Fact]
    public async Task Order_IsSessionDrainLeaveCheckpointFlushDisposeTerrainTransport()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        var client = await rig.JoinAsync("local", 1);
        var events = new List<string>();

        // A player the session does not own (its OnPlayerLeft never runs): only step 3b can take it offline.
        var ghost = rig.Runtime.Simulation.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, 99), "ghost");
        rig.Runtime.Simulation.ApplyServerOperation(ghost);
        var spy = new SpyParticipant(rig.Runtime, events);
        rig.Runtime.AddTickParticipant(spy);
        rig.Terrain.Last.OnShutdown = () =>
        {
            // The checkpoint of step 4 already holds every leave, and the writer is gone (the file is free).
            Assert.Equal(rig.Runtime.State.LastOpId.Value, (ulong)RawDb.Long(rig.Dir.WorldPath, "SELECT value FROM meta WHERE key = 'last_checkpoint_op_id'"));
            Assert.Empty(rig.Runtime.State.Players.Online);
            events.Add("terrain");
        };

        await rig.StopAsync();

        Assert.Equal(_fullOrder, rig.Runtime.CompletedShutdownSteps);
        Assert.Equal(["3a: session complete=True, online=1 (ghost, local left already)", "terrain"], events);
        Assert.Equal(ServerRuntimeState.Stopped, rig.Runtime.RuntimeState);
        Assert.Equal(0, rig.Runtime.ExitCode);
        Assert.Equal([DisconnectCode.HostLeft], client.Disconnections);
        Assert.True(rig.Terrain.Last.IsStoreClosed);
    }

    [Fact]
    public async Task Order_AfterTerrainTask_RunsAsStep7aBetweenTerrainAndTransport()
    {
        await using var rig = new ServerRig();
        var events = new List<string>();
        rig.Runtime.AddAfterTerrainShutdown(() =>
        {
            events.Add("game system");
            return Task.CompletedTask;
        });
        await rig.StartAsync();
        rig.Terrain.Last.OnShutdown = () => events.Add("terrain");

        await rig.StopAsync();

        Assert.Equal(
            [
                "1 session shutdown", "2 drain session and transport", "3a participants shutdown", "3b leave all online players", "4 checkpoint",
                "5 flush journal", "6 dispose writer", "7 shut terrain down", "7a shut game systems down", "8 dispose transport",
            ],
            rig.Runtime.CompletedShutdownSteps);
        Assert.Equal(["terrain", "game system"], events);
        Assert.Equal(0, rig.Runtime.ExitCode);
    }

    [Fact]
    public async Task AfterTerrainTasks_OneThrows_TheOthersStillRunAndTheStepIsLoggedOnly()
    {
        await using var rig = new ServerRig();
        var ran = new List<string>();
        rig.Runtime.AddAfterTerrainShutdown(() => throw new InvalidOperationException("first"));
        rig.Runtime.AddAfterTerrainShutdown(() =>
        {
            ran.Add("second");
            return Task.FromException(new InvalidOperationException("second fails too"));
        });
        rig.Runtime.AddAfterTerrainShutdown(() =>
        {
            ran.Add("third");
            return Task.CompletedTask;
        });
        await rig.StartAsync();

        await rig.StopAsync();

        Assert.Equal(["second", "third"], ran);
        Assert.Contains("7a shut game systems down", rig.Runtime.CompletedShutdownSteps);
        Assert.Contains("8 dispose transport", rig.Runtime.CompletedShutdownSteps);
        Assert.Equal(0, rig.Runtime.ExitCode);
    }

    [Fact]
    public async Task Order_DedicatedServer_SendsServerShutdownCode()
    {
        await using var rig = new ServerRig(mode: ProcessMode.DedicatedServer);
        await rig.StartAsync();
        var client = await rig.JoinAsync("remote", 1);

        await rig.StopAsync();

        Assert.Equal([DisconnectCode.ServerShutdown], client.Disconnections);
    }

    [Fact]
    public async Task Shutdown_NoJournalEnqueueAfterFlush()
    {
        var appended = new List<(ulong Op, Operation Operation, int StepsDone)>();
        ServerRuntime? runtime = null;
        var slow = new Net.Transport.InMemory.LinkConditions(TimeSpan.FromMilliseconds(200), TimeSpan.Zero, 0, 0, 1);
        await using var rig = new ServerRig(
            conditions: slow,
            configure: o => o with
            {
                JournalSinkDecorator = inner => new SpySink(inner, entry => appended.Add((entry.Id.Value, entry.Operation, runtime!.CompletedShutdownSteps.Count))),
            });
        runtime = rig.Runtime;
        await rig.StartAsync();
        var client = await rig.JoinAsync("local", 1);
        await rig.DigAsync(client, 3f, 1f);
        rig.StepMany(30);
        var before = appended.Count;
        var repliesBefore = client.Replies.Count;

        // Intents in flight while the server shuts down: right after step 1 and again during the drain.
        rig.Runtime.RequestShutdown();
        rig.Step();
        Assert.Contains("1 session shutdown", rig.Runtime.CompletedShutdownSteps);
        client.Session.SubmitIntent(rig.DigIntent(client.Player, -3f, 1f));
        rig.StepMany(3);
        client.Session.SubmitIntent(rig.DigIntent(client.Player, -3f, -2f));
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.IsStopped), "the server did not stop");

        // Nothing the late intents asked for was applied or journaled; only the leave operations of the shutdown reached the journal.
        var late = appended.Skip(before).ToList();
        Assert.NotEmpty(late);
        Assert.All(late, a => Assert.IsType<PlayerLeaveOp>(a.Operation));
        Assert.All(client.Replies.Skip(repliesBefore), r => Assert.Equal(RejectReason.ServerReadOnly, r.Reason));

        var flushDone = Array.IndexOf(_fullOrder, "5 flush journal") + 1;
        Assert.All(appended, a => Assert.True(a.StepsDone < flushDone, $"operation {a.Op} was appended after the flush"));

        // Everything appended is durable: a restart sees the same state.
        var live = rig.Hash;
        await using var again = new ServerRig(rig.Dir);
        await again.StartAsync();
        Assert.Equal(live, again.Hash);
        Assert.Equal(0, again.Terrain.Last.ReplayedEntries);
        await again.StopAsync();
    }

    [Fact]
    public async Task Shutdown_TransportDisposeBlocks_MainThreadNeverBlocksAndStopCompletesAfterDeadline()
    {
        GatedTransport? gated = null;
        await using var rig = new ServerRig(
            configure: o => o with { TransportDisposeTimeout = TimeSpan.FromSeconds(1) },
            wrapTransport: t => gated = new GatedTransport(t));
        await rig.StartAsync();
        try
        {
            var mainThread = Environment.CurrentManagedThreadId;
            rig.Runtime.RequestShutdown();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 2000 && !gated!.DisposeEntered; i++)
            {
                rig.Step();
                Thread.Sleep(1);
            }

            Assert.True(gated!.DisposeEntered, "step 8 never started");
            Assert.NotEqual(mainThread, gated.DisposeThreadId);
            Assert.False(rig.Runtime.IsStopped);

            // The gate stays shut: only the deadline can end the step, and every frame returns at once.
            for (var i = 0; i < 200 && !rig.Runtime.IsStopped; i++)
            {
                var frame = System.Diagnostics.Stopwatch.StartNew();
                rig.Step();
                Assert.True(frame.Elapsed < TimeSpan.FromSeconds(2), "a frame blocked on the transport");
            }

            Assert.True(rig.Runtime.IsStopped);
            Assert.Equal(_fullOrder, rig.Runtime.CompletedShutdownSteps);
            Assert.True(rig.Log.Contains(LogLevel.Warning, "8 dispose transport did not finish"));
            Assert.Equal(0, rig.Runtime.ExitCode);
        }
        finally
        {
            gated!.Gate.Set();
        }
    }

    [Fact]
    public async Task Shutdown_TerrainShutdownThrows_IsLoggedButExitCodeStaysZero()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        rig.Terrain.Last.OnShutdown = () => throw new InvalidOperationException("terrain boom (test)");

        await rig.StopAsync();

        Assert.Equal(_fullOrder, rig.Runtime.CompletedShutdownSteps);
        Assert.True(rig.Log.Contains(LogLevel.Error, "7 shut terrain down"));
        Assert.Equal(0, rig.Runtime.ExitCode);
    }

    [Fact]
    public async Task TerrainCloseFalse_StoreNotClosed()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        rig.Terrain.Last.CloseSucceeds = false;

        await rig.StopAsync();

        Assert.Equal(_fullOrder, rig.Runtime.CompletedShutdownSteps);
        Assert.False(rig.Terrain.Last.IsStoreClosed);
        Assert.True(rig.Log.Contains(LogLevel.Warning, "terrain store was left open"));
        Assert.Equal(0, rig.Runtime.ExitCode);
        rig.Terrain.Last.CloseStore();
    }

    [Fact]
    public async Task StepTimeouts_AdvanceWithLog()
    {
        // A hanging terrain shutdown: the sequence goes on after its deadline and the exit code stays 0.
        await using (var rig = new ServerRig(configure: o => o with { TerrainShutdownTimeout = TimeSpan.FromSeconds(1) }))
        {
            await rig.StartAsync();
            rig.Terrain.Last.HangShutdown = true;
            var stopping = rig.StopAsync();

            await stopping;

            Assert.Equal(_fullOrder, rig.Runtime.CompletedShutdownSteps);
            Assert.True(rig.Log.Contains(LogLevel.Warning, "7 shut terrain down did not finish"));
            Assert.Equal(0, rig.Runtime.ExitCode);
            rig.Terrain.Last.ReleaseShutdown();
        }

        // A hanging disk: the flush and the dispose of the writer run out of time; that is a write error, so the exit code is 70.
        using var release = new ManualResetEventSlim();
        var faults = new TestFaults();
        await using var hung = new ServerRig(
            faults: faults,
            configure: o => o with
            {
                ShutdownCheckpointTimeout = TimeSpan.FromSeconds(1),
                FlushTimeout = TimeSpan.FromSeconds(1),
                DisposeTimeout = TimeSpan.FromSeconds(1),
            });
        await hung.StartAsync();
        var client = await hung.JoinAsync("local", 1);
        hung.StepMany(5);
        faults.PauseAt(FaultPoint.BeforeJournalCommit, release);
        await hung.DigAsync(client, 2f, 2f);
        hung.StepMany(20);

        try
        {
            await hung.StopAsync();

            Assert.Equal(70, hung.Runtime.ExitCode);
            Assert.True(hung.Log.Contains(LogLevel.Warning, "5 flush journal did not finish"));
            Assert.True(hung.Log.Contains(LogLevel.Warning, "6 dispose writer did not finish"));
            Assert.Equal(_fullOrder.Length, hung.Runtime.CompletedShutdownSteps.Count);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task SecondRequest_Ignored()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        await rig.JoinAsync("local", 1);

        rig.Runtime.RequestShutdown();
        rig.Step();
        rig.Runtime.RequestShutdown();
        rig.Runtime.RequestShutdown();
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.IsStopped));

        Assert.Equal(_fullOrder, rig.Runtime.CompletedShutdownSteps);
        Assert.Equal(2, rig.Log.Entries.Count(e => e.Message.Contains("already running", StringComparison.Ordinal)));
        Assert.Equal(1, rig.Terrain.Last.ShutdownCalls);
        Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("Stopped;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostShutdown_RemoteClientSeesHostLeft()
    {
        var slow = new Net.Transport.InMemory.LinkConditions(TimeSpan.FromMilliseconds(100), TimeSpan.Zero, 0, 0, 1);
        await using var rig = new ServerRig(mode: ProcessMode.Host, conditions: slow);
        await rig.StartAsync();
        var local = await rig.JoinAsync("local", 1);
        var remote = await rig.JoinAsync("remote", 2);

        await rig.StopAsync();

        Assert.Equal([DisconnectCode.HostLeft], local.Disconnections);
        Assert.Equal([DisconnectCode.HostLeft], remote.Disconnections);
    }

    [Fact]
    public async Task Shutdown_ReadOnlyStart_SkipsLeaveCheckpointAndWriter()
    {
        using var dir = new TempDirectory();
        await using (var crashed = await Scenarios.CrashedRunAsync(dir, intents: 4))
        {
            await crashed.FinishCrashedRunAsync();
        }

        var factory = new FakeServerTerrainFactory(new FakeTimeProvider()) { SemanticsVersion = 2 };
        await using var readOnly = new ServerRig(dir, terrain: factory);
        Assert.IsType<WorldStartResult.StartedReadOnly>(await readOnly.StartAsync());

        await readOnly.StopAsync();

        Assert.Equal(
            ["1 session shutdown", "2 drain session and transport", "7 shut terrain down", "8 dispose transport"],
            readOnly.Runtime.CompletedShutdownSteps);
        Assert.Equal(0, readOnly.Runtime.ExitCode);
    }

    [Fact]
    public async Task StateHash_AfterGracefulStop_IsInTheStoppedLog()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        await rig.StopAsync();

        var entry = Assert.Single(rig.Log.Entries, e => e.Message.StartsWith("Stopped;", StringComparison.Ordinal));
        Assert.Equal(
            "Stopped; state hash=" + rig.Hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) + " last op=" + rig.Runtime.State.LastOpId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            entry.Message);
    }

    private sealed class SpyParticipant(ServerRuntime runtime, List<string> events) : IServerTickParticipant
    {
        public void OnTick(ServerRuntime owner)
        {
        }

        public void OnShutdown(ServerRuntime owner)
        {
            events.Add($"3a: session complete={runtime.Session.IsShutdownComplete}, online={runtime.State.Players.Online.Count()} (ghost, local left already)");
        }
    }

    private sealed class SpySink(IJournalSink inner, Action<JournalEntry> seen) : IJournalSink
    {
        public void Append(JournalEntry entry)
        {
            seen(entry);
            inner.Append(entry);
        }
    }
}
