using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Integration.Tests.Support;
using Horyzont.Persistence;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static Horyzont.Integration.Tests.Tools.ToolTestHelpers;

namespace Horyzont.Integration.Tests.Tools;

/// <summary>
/// Two clients drive the tools at random through the whole stack while a persistence writer records the journal on a SQLite file:
/// the live state, the full replay of the journal and the checkpoint plus the journal tail agree.
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class ToolSessionPersistenceTests : IDisposable
{
    private const int Actions = 2000;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "horyzont-tests", Guid.NewGuid().ToString("N"));

    public ToolSessionPersistenceTests() => Directory.CreateDirectory(_dir);

    private string WorldPath => Path.Combine(_dir, "world.db");

    public void Dispose()
    {
        // Pooled read connections keep the file open on Windows until this world's pool is closed.
        new TerrainBlockStore(WorldPath).Close();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Safe to leave: the directory is unique and under the temp directory.
        }
    }

    [Theory]
    [InlineData(20261009)]
    [InlineData(7)]
    public async Task ToolSession_RandomActions_LiveEqualsReplayEqualsCheckpointPlusTail(int seed)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        var options = new PersistenceOptions { GameVersion = "k2-13", BackupInterval = Timeout.InfiniteTimeSpan };
        WorldCreator.Create(WorldPath, WorldFactory.CreateDefault(), options, time);
        var loadOptions = new WorldLoadOptions { BackupsDirectory = Path.Combine(_dir, "backups") };
        var loaded = WorldLoader.Load(WorldPath, loadOptions, time, NullLogger.Instance);
        await using var writer = new PersistenceWriter(
            WorldDatabase.Open(WorldPath), options, time, new ArmedFaults(), NullLogger<PersistenceWriter>.Instance);
        var all = new List<JournalEntry>();
        using var rig = new ToolRig(journal: new TeeSink(new JournalSink(writer), all), state: loaded.State, time: time);
        var clients = new[] { rig.AddClient("alice", 1), rig.AddClient("bob", 2) };
        var random = new Random(seed);

        for (var i = 0; i < Actions; i++)
        {
            if (i == Actions / 2)
            {
                await CheckpointAsync(rig, writer);
            }

            if (i % 40 == 0)
            {
                Shuffle(rig, clients, random);
            }

            var client = clients[random.Next(clients.Length)];
            client.Update(RandomInput(random), AimRandomly(rig, client, random));
            rig.Pump(random.Next(1, 4));
        }

        rig.Pump(120);
        Assert.True(await writer.FlushAsync());
        Drain(writer, rig.Sim.State);

        // The run must have used every tool, not only refused actions.
        foreach (var kind in new[] { typeof(TerrainEditOp), typeof(ConstructCreateOp), typeof(BlockSetOp), typeof(BlockClearOp) })
        {
            Assert.Contains(all, e => e.Operation.GetType() == kind);
        }

        var liveHash = StateHasher.Hash(rig.Sim.State);
        var liveLast = rig.Sim.State.LastOpId;

        var replayed = new ServerSimulation(
            WorldFactory.CreateDefault(), new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        foreach (var entry in all)
        {
            replayed.Replay(entry);
        }

        Assert.Equal(liveLast, replayed.State.LastOpId);
        Assert.Equal(liveHash, StateHasher.Hash(replayed.State));

        var recovered = WorldLoader.Load(WorldPath, loadOptions, new FakeTimeProvider(), NullLogger.Instance);
        Assert.True(recovered.LastCheckpointOpId > OpId.None, "recovery must start from the checkpoint");
        Assert.NotEmpty(recovered.JournalTail);
        var store = new TerrainBlockStore(WorldPath);
        var recoveredTerrain = new FakeTerrainService { BlockSource = key => store.TryLoad(key, out _, out var data) ? data : null };
        var fresh = new ServerSimulation(recovered.State, recoveredTerrain, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        foreach (var entry in recovered.JournalTail)
        {
            fresh.Replay(entry);
        }

        Assert.Equal(liveLast, fresh.State.LastOpId);
        Assert.Equal(liveHash, StateHasher.Hash(fresh.State));
        foreach (var (key, bytes) in rig.Terrain.Blocks)
        {
            Assert.Equal(bytes, recoveredTerrain.GetBlockData(key));
        }
    }

    private static async Task CheckpointAsync(ToolRig rig, PersistenceWriter writer)
    {
        Assert.True(await writer.FlushAsync());
        Drain(writer, rig.Sim.State);
        var core = rig.Sim.State.CaptureCheckpoint();
        var blocks = core.TerrainVersions.Select(v => new TerrainBlockPayload(v.Key, v.Version, rig.Terrain.GetBlockData(v.Key))).ToList();
        Assert.True(await writer.CheckpointAsync(new CheckpointRequest(core, blocks)));
        Drain(writer, rig.Sim.State);
    }

    private static void Drain(PersistenceWriter writer, WorldState state)
    {
        while (writer.TryDequeueEvent(out var evt))
        {
            switch (evt)
            {
                case CheckpointCommitted committed:
                    state.CompleteCheckpoint(committed.Request.Core);
                    break;
                case CheckpointFailed failed:
                    state.RequeueAfterFailedCheckpoint(failed.Request.Core);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Moves the players on the server to random places near the ground (the physics of K2-11 does this every tick).</summary>
    private static void Shuffle(ToolRig rig, ToolRig.ToolClient[] clients, Random random)
    {
        foreach (var client in clients)
        {
            var state = client.State;
            var feet = rig.Ground(((float)random.NextDouble() * 12f) - 6f, ((float)random.NextDouble() * 12f) - 6f) + new Vector3(0f, 0.1f, 0f);
            Assert.True(rig.Sim.UpdatePlayerKinematics(client.Player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
        }
    }

    private static ToolInputFrame RandomInput(Random random) =>
        Input(
            primaryPressed: random.Next(10) < 3,
            secondaryPressed: random.Next(10) < 4,
            select: random.Next(8) == 0 ? (random.Next(2) == 0 ? ToolKind.TerrainManipulator : ToolKind.Builder) : null,
            radius: random.Next(-2, 3),
            palette: random.Next(6) == 0 ? 1 : 0,
            material: random.Next(6) == 0 ? 1 : 0,
            yaw: random.Next(-1, 2),
            up: random.Next(6) == 0 ? 1 : 0);

    private static AimRay AimRandomly(ToolRig rig, ToolRig.ToolClient client, Random random)
    {
        var constructs = rig.Sim.State.Constructs.All;
        if (constructs.Count > 0 && random.Next(10) < 4)
        {
            var construct = constructs[random.Next(constructs.Count)];
            var point = construct.CellCenterToParent(new Core.World.Constructs.CellPos(0, random.Next(2), 0));
            if (Vector3.Distance(point, client.Eye) > 0.5f)
            {
                return client.AimAt(point + new Vector3(((float)random.NextDouble() * 0.6f) - 0.3f, 0f, ((float)random.NextDouble() * 0.6f) - 0.3f));
            }
        }

        var x = ((float)random.NextDouble() * 12f) - 6f;
        var z = ((float)random.NextDouble() * 12f) - 6f;
        var target = rig.Ground(x, z);
        return Vector3.Distance(target, client.Eye) > 0.5f ? client.AimAt(target) : client.AimAt(rig.Ground(x + 3f, z));
    }

    private sealed class TeeSink(IJournalSink inner, List<JournalEntry> copy) : IJournalSink
    {
        public void Append(JournalEntry entry)
        {
            copy.Add(entry);
            inner.Append(entry);
        }
    }
}
