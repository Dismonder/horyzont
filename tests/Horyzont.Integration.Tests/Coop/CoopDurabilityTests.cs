using Horyzont.Bots.Scripting;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Integration.Tests.Support;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Integration.Tests.Coop;

/// <summary>
/// Criterion 7 of the planet: a restart keeps every change, and a dead process loses at most the last journal batch without
/// duplicates (virtual version: the write of a batch fails instead of the process being killed).
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class CoopDurabilityTests
{
    [Fact]
    public async Task Coop_JournalBatchLostAtCrash_RecoveredStateLosesOnlyLastBatch_NoDuplicates()
    {
        var faults = new ArmedFaults();
        await using var rig = new CoopRig(faults: faults, hashEveryOperation: true);
        await rig.StartServerAsync();
        AddCrowd(rig);
        await rig.RunAsync(600);

        // A checkpoint makes everything so far durable; then the bots work on for a while and the writer catches up.
        var committedCheckpoints = rig.Terrain.Last.CommittedCheckpoints;
        rig.Server.RequestCheckpoint();
        Assert.True(await rig.RunUntilAsync(() => rig.Terrain.Last.CommittedCheckpoints > committedCheckpoints), "no checkpoint");
        await rig.RunAsync(240);
        await rig.DrainWriterAsync();
        var armedAt = rig.Server.DurableOpId;

        // The process dies right after the next journal commit: the rows are on disk, the writer is gone.
        // Every frame is recorded with its virtual time and last operation: the crash happened at the latest at the last frame
        // and at the earliest at the one before, so an operation made at least one flush interval before that earlier frame must be on disk.
        var history = new List<(DateTimeOffset At, OpId Last)> { (rig.Time.GetUtcNow(), armedAt) };
        faults.Arm(FaultPoint.AfterJournalCommit);
        for (var i = 0; i < 3000 && !faults.Crashed; i++)
        {
            rig.Step();
            history.Add((rig.Time.GetUtcNow(), rig.Server.State.LastOpId));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        Assert.True(faults.Crashed, "the writer never reached the fault point");
        var crashNotBefore = history.Count >= 2 ? history[^2].At : history[0].At;
        var acknowledgedBeforeCrash = history.LastOrDefault(entry => entry.At <= crashNotBefore - CoopRig.Persistence.FlushInterval, history[0]).Last;

        // The process does not know it is dead for a moment longer: what the bots do now never reaches the disk.
        await rig.RunAsync(120);
        var liveLast = rig.Server.State.LastOpId;

        var committedLast = new OpId(MaxJournalOpId(rig.WorldPath));
        Assert.True(committedLast >= armedAt, "an operation that was durable before the crash is lost");
        Assert.True(
            committedLast >= acknowledgedBeforeCrash,
            $"operation {acknowledgedBeforeCrash.Value} was made more than one flush interval before the crash but only {committedLast.Value} is on disk");
        Assert.True(committedLast <= liveLast);

        // Recovery: load the file, replay the tail into a fresh simulation, compare with the live state at that operation.
        var recovered = WorldLoader.Load(
            rig.WorldPath, new WorldLoadOptions { BackupsDirectory = Path.Combine(rig.DirectoryPath, "saves", "backups") },
            new FakeTimeProvider(), NullLogger.Instance);
        Assert.True(recovered.LastCheckpointOpId > OpId.None, "recovery must start from the checkpoint");
        var store = new TerrainBlockStore(rig.WorldPath);
        var terrain = new FakeTerrainService { BlockSource = key => store.TryLoad(key, out _, out var data) ? data : null };
        var fresh = new ServerSimulation(recovered.State, terrain, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);

        // No duplicates and no holes: the tail is exactly the operations after the checkpoint, each once, in order.
        var expected = recovered.LastCheckpointOpId.Value;
        foreach (var entry in recovered.JournalTail)
        {
            Assert.Equal(++expected, entry.Id.Value);
            fresh.Replay(entry);
        }

        Assert.Equal(committedLast.Value, expected);
        Assert.Equal(committedLast, fresh.State.LastOpId);
        Assert.Equal(rig.Hashes!.HashAt[committedLast], StateHasher.Hash(fresh.State));

        // What was lost is only a tail of operations that were never durable: all of them come after the last committed one.
        var lost = liveLast.Value - committedLast.Value;
        Assert.True(lost > 0, "the operations made after the crash cannot be on disk");
        Assert.True(lost <= liveLast.Value - armedAt.Value, $"{lost} operations lost, but only {liveLast.Value - armedAt.Value} were made after the arming");
    }

    [Fact]
    public async Task Coop_GracefulRestart_BotsReconnect_StateHashAndLastOpUnchanged()
    {
        await using var rig = new CoopRig();
        await rig.StartServerAsync();
        AddCrowd(rig);
        await rig.RunAsync(600);
        Assert.All(rig.Bots, bot => Assert.True(bot.IsInGame));
        Assert.True(rig.Server.State.LastOpId.Value > 8);

        await rig.StopServerAsync();
        var hashAtStop = rig.Hash;
        var lastOpAtStop = rig.Server.State.LastOpId;
        await rig.RestartServerAsync();

        // The new run starts from exactly the state that was stopped.
        Assert.Equal(hashAtStop, rig.Hash);
        Assert.Equal(lastOpAtStop, rig.Server.State.LastOpId);

        // The bots notice the shutdown, wait, connect again, and the world goes on from the next operation.
        Assert.True(await rig.RunUntilAsync(() => rig.Bots.All(b => b.IsInGame && b.Stats.Joins == 2), 3000), "the bots did not return");
        Assert.All(rig.Bots, bot => Assert.True(bot.Stats.Reconnects >= 1, bot.Options.Name + " did not reconnect"));
        Assert.Equal(8, rig.Server.Session.PlayersInGame.Count);
        var before = rig.Server.State.LastOpId;
        await rig.RunAsync(300);
        Assert.True(rig.Server.State.LastOpId > before, "no edits after the restart");
        await rig.StopServerAsync();
    }

    private static void AddCrowd(CoopRig rig)
    {
        var roles = new[]
        {
            BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Builder,
            BotRole.Explorer, BotRole.Explorer, BotRole.Abuser,
        };
        for (var i = 0; i < roles.Length; i++)
        {
            rig.AddBot(roles[i], seed: 100UL + (ulong)i);
        }
    }

    private static ulong MaxJournalOpId(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(op_id), 0) FROM journal";
        return (ulong)(long)command.ExecuteScalar()!;
    }
}
