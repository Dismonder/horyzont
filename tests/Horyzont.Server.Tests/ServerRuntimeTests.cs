using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Writing;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests;

[Collection(SqlitePools.Name)]
public sealed class ServerRuntimeTests
{
    [Fact]
    public async Task AddClientMessageHandler_AfterStart_RejectsRegistration()
    {
        await using var rig = new ServerRig();
        rig.Runtime.AddClientMessageHandler(new MessageHandler());
        await rig.StartAsync();
        Assert.Throws<InvalidOperationException>(() => rig.Runtime.AddClientMessageHandler(new MessageHandler()));
        await rig.StopAsync();
    }

    [Fact]
    public async Task AddClientMessageHandler_BeforeStart_DispatchesSessionMessage()
    {
        await using var rig = new ServerRig();
        var handler = new MessageHandler();
        rig.Runtime.AddClientMessageHandler(handler);
        await rig.StartAsync();
        var client = await rig.JoinAsync("local", 1);
        client.Session.Send(new Horyzont.Core.Protocol.Messages.SnapshotAck(42));
        rig.StepMany(2);
        Assert.Equal(client.Player, handler.Player);
        Assert.Equal(42u, Assert.IsType<Horyzont.Core.Protocol.Messages.SnapshotAck>(handler.Message).SnapshotId);
        await rig.StopAsync();
    }

    private sealed class MessageHandler : IServerClientMessageHandler
    {
        public PlayerId Player { get; private set; }
        public Horyzont.Core.Protocol.Messages.Message? Message { get; private set; }
        public bool Handle(PlayerId player, Horyzont.Core.Protocol.Messages.Message message)
        {
            Player = player;
            Message = message;
            return true;
        }
    }

    [Fact]
    public async Task Start_NewWorld_CreatesDatabaseCheckpointAndStartupBackup()
    {
        await using var rig = new ServerRig();

        var result = await rig.StartAsync();

        Assert.IsType<WorldStartResult.Started>(result);
        Assert.Equal(ServerRuntimeState.Running, rig.Runtime.RuntimeState);
        Assert.True(File.Exists(rig.Dir.WorldPath));
        Assert.Equal(1, RawDb.Long(rig.Dir.WorldPath, "SELECT COUNT(*) FROM checkpoints"));
        Assert.Single(Directory.GetFiles(rig.Dir.BackupsPath, "*-startup.db"));
        Assert.Equal(1, rig.Terrain.Last.CommittedCheckpoints);
        Assert.Equal("1|voxel=test-voxel", RawDb.Scalar(rig.Dir.WorldPath, "SELECT value FROM meta WHERE key = 'terrain_semantics'"));
        await rig.StopAsync();
    }

    [Fact]
    public async Task Start_TerrainCreatedFromLoadedState()
    {
        using var dir = new TempDirectory();
        int versions;
        await using (var first = new ServerRig(dir))
        {
            await first.StartAsync();
            var client = await first.JoinAsync("local", 1);
            await first.DigAsync(client, 5f, 1.5f);
            first.StepMany(20);
            await first.DigAsync(client, -6f, 2f);
            await first.StopAsync();
            versions = first.Runtime.State.TerrainVersions.Count;
            Assert.True(versions > 0);
        }

        await using var second = new ServerRig(dir);
        Assert.Empty(second.Terrain.Calls);

        await second.StartAsync();

        var call = Assert.Single(second.Terrain.Calls);
        Assert.True(call.FileExisted);
        Assert.Equal(versions, call.TerrainVersionCount);
        Assert.Equal(second.Runtime.State.LastOpId, call.LastOpId);
        await second.StopAsync();
    }

    [Fact]
    public async Task Restart_AfterEdits_SameStateHash()
    {
        using var dir = new TempDirectory();
        ulong liveHash;
        FakeServerTerrain firstTerrain;
        await using (var first = new ServerRig(dir))
        {
            await first.StartAsync();
            var client = await first.JoinAsync("local", 1);
            await Scenarios.DigManyAsync(first, client, 50);
            await first.StopAsync();
            liveHash = first.Hash;
            firstTerrain = first.Terrain.Last;
            Assert.Empty(first.Runtime.State.Players.Online);
            Assert.True(first.Runtime.State.TerrainVersions.Count > 0);
        }

        await using var second = new ServerRig(dir);
        var result = await second.StartAsync();

        Assert.IsType<WorldStartResult.Started>(result);
        Assert.Equal(liveHash, second.Hash);
        Assert.Equal(0, second.Terrain.Last.ReplayedEntries);
        foreach (var version in second.Runtime.State.TerrainVersions.All)
        {
            Assert.Equal(firstTerrain.Terrain.GetBlockData(version.Key), second.Terrain.Last.Terrain.GetBlockData(version.Key));
        }

        await second.StopAsync();
    }

    [Fact]
    public async Task Restart_AfterCrashWithoutShutdown_ReplaysTailAndLeavesPlayers()
    {
        using var referenceDir = new TempDirectory();
        var reference = await RunScenarioAsync(referenceDir, crash: false);
        using var crashDir = new TempDirectory();

        var crashed = await RunScenarioAsync(crashDir, crash: true);

        // The reference run left cleanly; the crashed run was killed with both players online and the leave operations lost.
        Assert.Equal(reference.Hash, crashed.HashAfterRestart);
        Assert.True(crashed.Replayed > 30);
        Assert.Equal(2, crashed.LeftAtStart);
        Assert.Equal(reference.Hash, crashed.HashAfterSecondRestart);
    }

    [Fact]
    public async Task Start_CorruptedDatabase_ReturnsCorruptedWithBackups()
    {
        using var dir = new TempDirectory();
        await using (var first = new ServerRig(dir))
        {
            await first.StartAsync();
            await first.StopAsync();
        }

        File.Delete(dir.WorldPath + "-wal");
        File.Delete(dir.WorldPath + "-shm");
        await File.WriteAllBytesAsync(dir.WorldPath, new byte[4096], TestContext.Current.CancellationToken);
        await using var second = new ServerRig(dir);

        var result = await second.StartAsync();

        var corrupted = Assert.IsType<WorldStartResult.Corrupted>(result);
        Assert.Single(corrupted.Backups);
        Assert.EndsWith("-startup.db", corrupted.Backups[0], StringComparison.Ordinal);
        Assert.Equal(ServerRuntimeState.Stopped, second.Runtime.RuntimeState);
        Assert.Equal(3, second.Runtime.ExitCode);
        Assert.Throws<InvalidOperationException>(() => second.Runtime.Session);
        Assert.Empty(second.Terrain.Created);
    }

    [Fact]
    public async Task Start_JournalOpIdGap_ReturnsCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var crashed = await Scenarios.CrashedRunAsync(dir, intents: 8))
        {
            var ids = RawDb.JournalIds(dir.WorldPath);
            Assert.True(ids.Count > 4);
            RawDb.Execute(dir.WorldPath, "DELETE FROM journal WHERE op_id = $id;", ("$id", ids[2]));
            await crashed.FinishCrashedRunAsync();
        }

        await using var second = new ServerRig(dir);

        var result = await second.StartAsync();

        var corrupted = Assert.IsType<WorldStartResult.Corrupted>(result);
        Assert.NotEmpty(corrupted.Message);
        Assert.NotEmpty(corrupted.Backups);
        Assert.Equal(ServerRuntimeState.Stopped, second.Runtime.RuntimeState);
        Assert.Empty(second.Terrain.Created);
    }

    [Fact]
    public async Task Start_ReplayFails_ReturnsReplayFailedWithOpId()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider();
        var options = new PersistenceOptions { GameVersion = "k2-10-test", BackupInterval = Timeout.InfiniteTimeSpan };
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), options, time);
        await using (var writer = new PersistenceWriter(
            WorldDatabase.Open(dir.WorldPath), options, time, NoFaults.Instance, NullLogger<PersistenceWriter>.Instance))
        {
            // A continuous journal whose first entry breaks its precondition: nobody is online to leave.
            var leave = new PlayerLeaveOp(new PlayerId(1), FrameRef.Of(WorldFactory.HomePlanet), 0, new System.Numerics.Vector3(0, 4000, 0), System.Numerics.Quaternion.Identity, 0);
            writer.EnqueueJournal(new JournalEntry(new OpId(1), default, null, leave));
            Assert.True(await writer.FlushAsync());
        }

        await using var rig = new ServerRig(dir, time);

        var result = await rig.StartAsync();

        var failed = Assert.IsType<WorldStartResult.ReplayFailed>(result);
        Assert.Equal(new OpId(1), failed.OpId);
        Assert.Equal(4, rig.Runtime.ExitCode);
        Assert.Equal(ServerRuntimeState.Stopped, rig.Runtime.RuntimeState);
        Assert.Equal(1, rig.Terrain.Last.ShutdownCalls);
        Assert.True(rig.Terrain.Last.IsStoreClosed);
        Assert.Throws<InvalidOperationException>(() => rig.Runtime.Session);

        // The writer was disposed: the file can be opened exclusively and holds no checkpoint of this attempt.
        Assert.Equal(1, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM journal"));
    }

    [Fact]
    public async Task Start_TerrainSemanticsMismatch_StartsReadOnlyWithoutWrites()
    {
        using var dir = new TempDirectory();
        await using (var crashed = await Scenarios.CrashedRunAsync(dir, intents: 6))
        {
            await crashed.FinishCrashedRunAsync();
        }

        var journalBefore = RawDb.JournalIds(dir.WorldPath);
        var checkpointsBefore = RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM checkpoints");
        Assert.NotEmpty(journalBefore);

        // A newer build with other terrain edit semantics.
        var factory = new FakeServerTerrainFactory(new FakeTimeProvider()) { SemanticsVersion = 2 };
        await using (var readOnly = new ServerRig(dir, terrain: factory))
        {
            var result = await readOnly.StartAsync();

            var started = Assert.IsType<WorldStartResult.StartedReadOnly>(result);
            Assert.Equal(ReadOnlyReason.TerrainSemanticsMismatch, started.Reason);
            Assert.Contains("1|voxel=test-voxel", started.Message, StringComparison.Ordinal);
            Assert.Contains("2|voxel=test-voxel", started.Message, StringComparison.Ordinal);
            Assert.Equal(ServerRuntimeState.SafeMode, readOnly.Runtime.RuntimeState);
            Assert.True(readOnly.Runtime.IsReadOnlyStart);
            Assert.Equal(0, factory.Last.ReplayedEntries);

            // Joins are refused; nothing is written.
            var client = readOnly.AddClient("local", 1);
            Assert.True(await readOnly.RunUntilAsync(() => client.Session.State == Net.Session.ClientState.Disconnected));
            Assert.Null(client.Hello);
            readOnly.StepMany(30);
            Assert.Equal(journalBefore, RawDb.JournalIds(dir.WorldPath));
            Assert.Equal(checkpointsBefore, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM checkpoints"));
            Assert.Empty(Directory.GetFiles(dir.BackupsPath, "*-pre-recover.db"));

            await readOnly.StopAsync();
            Assert.Equal(0, readOnly.Runtime.ExitCode);
            Assert.Equal(journalBefore, RawDb.JournalIds(dir.WorldPath));
            Assert.Equal(checkpointsBefore, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM checkpoints"));
        }

        // The owner's explicit choice: drop the tail after a backup and start from the checkpoint.
        var recoverFactory = new FakeServerTerrainFactory(new FakeTimeProvider()) { SemanticsVersion = 2 };
        await using var recover = new ServerRig(dir, terrain: recoverFactory, configure: o => o with { RecoverFromCheckpoint = true });

        var recovered = await recover.StartAsync();

        Assert.IsType<WorldStartResult.Started>(recovered);
        Assert.Equal(ServerRuntimeState.Running, recover.Runtime.RuntimeState);
        Assert.Single(Directory.GetFiles(dir.BackupsPath, "*-pre-recover.db"));
        Assert.Equal(0, recoverFactory.Last.ReplayedEntries);
        Assert.Equal("2|voxel=test-voxel", RawDb.Scalar(dir.WorldPath, "SELECT value FROM meta WHERE key = 'terrain_semantics'"));
        await recover.StopAsync();
    }

    [Fact]
    public async Task Tick_Every60s_CheckpointsOnTickBoundary()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        var terrain = rig.Terrain.Last;
        Assert.Single(terrain.Requests);

        rig.StepMany(100);
        Assert.Single(terrain.Requests);

        rig.Time.Advance(TimeSpan.FromSeconds(60));
        rig.Runtime.Tick();
        rig.Runtime.Tick();
        rig.Runtime.Tick();

        // The first one is in flight (or just done); a second one never starts inside the same interval.
        Assert.True(await rig.RunUntilAsync(() => terrain.CommittedCheckpoints == 2));
        rig.StepMany(100);
        Assert.Equal(2, terrain.Requests.Count);
        Assert.Equal(2, terrain.CommittedCheckpoints);
        await rig.StopAsync();
    }

    [Fact]
    public async Task Tick_NeedsEarlyCheckpoint_CheckpointsBeforeInterval()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        var terrain = rig.Terrain.Last;

        terrain.ForceEarlyCheckpoint = true;
        rig.Step();
        terrain.ForceEarlyCheckpoint = false;

        Assert.Equal(2, terrain.Requests.Count);
        Assert.True(await rig.RunUntilAsync(() => terrain.CommittedCheckpoints == 2));
        rig.StepMany(50);
        Assert.Equal(2, terrain.Requests.Count);
        await rig.StopAsync();
    }

    [Fact]
    public async Task CheckpointFailed_RequeuesAndRetries()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        var terrain = rig.Terrain.Last;
        var client = await rig.JoinAsync("local", 1);
        await Scenarios.DigManyAsync(rig, client, 4);
        Assert.True(rig.Runtime.State.TerrainVersions.Count > 0);

        rig.Faults.DiskErrorAt(FaultPoint.BeforeCheckpointCommit);
        rig.Time.Advance(TimeSpan.FromSeconds(60));
        Assert.True(await rig.RunUntilAsync(() => terrain.FailedCheckpoints == 1));
        var failed = terrain.Requests[^1];

        Assert.False(rig.Runtime.State.IsCheckpointInFlight);
        Assert.Equal(ServerRuntimeState.SafeMode, rig.Runtime.RuntimeState);

        // `save` clears the writer fault and the retry carries the same blocks.
        var committedBefore = terrain.CommittedCheckpoints;
        rig.Runtime.RequestCheckpoint();
        Assert.True(await rig.RunUntilAsync(() => terrain.CommittedCheckpoints == committedBefore + 1));
        var retried = terrain.Requests[^1];
        Assert.Equal(failed.TerrainBlocks.Select(b => (b.Key, b.Version)), retried.TerrainBlocks.Select(b => (b.Key, b.Version)));
        Assert.Equal(ServerRuntimeState.Running, rig.Runtime.RuntimeState);
        Assert.Equal(
            RawDb.Long(rig.Dir.WorldPath, "SELECT COUNT(*) FROM terrain_blocks"),
            retried.TerrainBlocks.Count);
        await rig.StopAsync();
    }

    [Fact]
    public async Task WriterFaulted_EntersSafeModeAndRejectsIntents()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        var client = await rig.JoinAsync("local", 1);
        rig.StepMany(30);
        rig.Faults.DiskErrorAt(FaultPoint.BeforeJournalCommit);
        await Scenarios.DigManyAsync(rig, client, 1);

        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.RuntimeState == ServerRuntimeState.SafeMode));
        var hash = rig.Hash;
        var reply = await rig.DigAsync(client, 3f, 3f);

        Assert.Equal(RejectReason.ServerReadOnly, reply.Reason);
        Assert.Equal(OpId.None, reply.AppliedOpId);
        Assert.Equal(hash, rig.Hash);

        // A new player cannot join in safe mode.
        var late = rig.AddClient("late", 2);
        Assert.True(await rig.RunUntilAsync(() => late.Session.State == Net.Session.ClientState.Disconnected));
        Assert.Null(late.Hello);

        rig.Runtime.RequestCheckpoint();
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.RuntimeState == ServerRuntimeState.Running));
        Assert.True(rig.Log.Contains(LogLevel.Information, "leaving safe mode"));
        var after = await rig.DigAsync(client, 4f, 2f);
        Assert.Equal(RejectReason.None, after.Reason);
        await rig.StopAsync();
        Assert.Equal(0, rig.Runtime.ExitCode);
    }

    [Fact]
    public async Task SimulationFaultInHook_StopsWithoutCheckpoint()
    {
        using var dir = new TempDirectory();
        await using (var rig = new ServerRig(dir))
        {
            await rig.StartAsync();
            var terrain = rig.Terrain.Last;
            var client = await rig.JoinAsync("local", 1);
            await Scenarios.DigManyAsync(rig, client, 3);
            var requests = terrain.Requests.Count;
            var checkpoints = RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM checkpoints");

            terrain.Terrain.ThrowOnApply = new InvalidOperationException("voxel engine failed (test)");
            rig.StepMany(20);
            client.Session.SubmitIntent(rig.DigIntent(client.Player, 2f, 2f));

            Assert.True(await rig.RunUntilAsync(() => rig.Runtime.IsStopped));
            Assert.Equal(ServerRuntimeState.Faulted, rig.Runtime.RuntimeState);
            Assert.Equal(70, rig.Runtime.ExitCode);
            Assert.Equal(requests, terrain.Requests.Count);
            Assert.Equal(checkpoints, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM checkpoints"));
            Assert.DoesNotContain("3a participants shutdown", rig.Runtime.CompletedShutdownSteps);
            Assert.DoesNotContain("4 checkpoint", rig.Runtime.CompletedShutdownSteps);
            Assert.Contains("5 flush journal", rig.Runtime.CompletedShutdownSteps);
            Assert.Equal(1, terrain.ShutdownCalls);
            Assert.NotEmpty(RawDb.JournalIds(dir.WorldPath));
        }

        // The journal written before the fault is durable and replays; the player was online and is left at start.
        await using var restarted = new ServerRig(dir);
        Assert.IsType<WorldStartResult.Started>(await restarted.StartAsync());
        Assert.Equal(1, restarted.Runtime.State.Players.Count);
        Assert.Empty(restarted.Runtime.State.Players.Online);
        await restarted.StopAsync();
    }

    [Fact]
    public async Task Join_ThrowInApply_NothingApplied()
    {
        // No planet to spawn on: PreparePlayerJoin throws before anything is applied (E5: prepare and apply are one call).
        await using var rig = new ServerRig(
            createWorld: () => Core.World.WorldState.CreateNew(new WorldId(Guid.NewGuid()), 1, [], ["core:stone_block"], []));
        await rig.StartAsync();
        var hash = rig.Hash;
        var lastOp = rig.Runtime.State.LastOpId;

        var client = rig.AddClient("local", 1);
        Assert.True(await rig.RunUntilAsync(() => client.Session.State == Net.Session.ClientState.Disconnected));

        Assert.Null(client.Hello);
        Assert.Equal(hash, rig.Hash);
        Assert.Equal(lastOp, rig.Runtime.State.LastOpId);
        Assert.Empty(rig.Runtime.State.Players.All);
        Assert.Empty(rig.Runtime.Session.PlayersInGame);
        await rig.StopAsync();
    }

    [Fact]
    public async Task Constructor_ClientMode_Throws()
    {
        using var dir = new TempDirectory();
        await using var rig = new ServerRig(dir);

        Assert.Throws<ArgumentException>(() => new ServerRuntime(
            ProcessMode.Client, rig.Options, rig.ServerTransport, rig.Terrain, WorldFactory.CreateDefault, rig.Time, NullLoggerFactory.Instance));
        await Task.CompletedTask;
    }

    /// <summary>Runs a world with two players and 30 terrain edits, then either ends cleanly or dies with both players online.</summary>
    private static async Task<(ulong Hash, ulong HashAfterRestart, ulong HashAfterSecondRestart, int Replayed, int LeftAtStart)> RunScenarioAsync(
        TempDirectory dir, bool crash)
    {
        if (!crash)
        {
            await using var rig = new ServerRig(dir);
            await rig.StartAsync();
            var a = await rig.JoinAsync("local", 1);
            var b = await rig.JoinAsync("remote", 2);
            await Scenarios.RunIntentsAsync(rig, a, b);
            a.Session.Disconnect();
            b.Session.Disconnect();
            Assert.True(await rig.RunUntilAsync(() => !rig.Runtime.State.Players.Online.Any()));
            await rig.StopAsync();
            return (rig.Hash, 0, 0, 0, 0);
        }

        ulong hashAfterRestart;
        int replayed;
        int left;
        await using (var crashed = await Scenarios.CrashedRunAsync(dir, intents: 30))
        {
            await crashed.FinishCrashedRunAsync();
        }

        await using (var restarted = new ServerRig(dir))
        {
            Assert.IsType<WorldStartResult.Started>(await restarted.StartAsync());
            hashAfterRestart = restarted.Hash;
            replayed = restarted.Terrain.Last.ReplayedEntries;
            left = restarted.Runtime.State.Players.All.Count;
            Assert.Empty(restarted.Runtime.State.Players.Online);
            await restarted.StopAsync();
        }

        await using var again = new ServerRig(dir);
        await again.StartAsync();
        var second = again.Hash;
        await again.StopAsync();
        return (0, hashAfterRestart, second, replayed, left);
    }
}
