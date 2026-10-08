using System.Globalization;

using Horyzont.Core.Simulation;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

// Crash test host (K1-05): opens an existing world, plays WorldScript(seed) with real time, requests a checkpoint every
// 100 operations when none is in flight, and prints "DURABLE <opId>" after every JournalCommitted. The test kills it at a
// random moment and compares the reopened world with the oracle.
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: Horyzont.Persistence.CrashHost <world.db> <seed> [maxSteps]");
    return 64;
}

var path = args[0];
var seed = ulong.Parse(args[1], CultureInfo.InvariantCulture);
var maxSteps = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 2_000_000;
const ulong CheckpointEvery = 100;

var time = TimeProvider.System;
var options = new PersistenceOptions { GameVersion = "crash-host", BackupInterval = Timeout.InfiniteTimeSpan };
var loaded = WorldLoader.Load(path, new WorldLoadOptions { BackupsDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "backups") }, time, NullLogger.Instance);
var store = new TerrainBlockStore(path);
var terrain = new FakeTerrainService { BlockSource = key => store.TryLoad(key, out _, out var data) ? data : null };
await using var writer = new PersistenceWriter(WorldDatabase.Open(path), options, time, NoFaults.Instance, NullLogger<PersistenceWriter>.Instance);
var sim = new ServerSimulation(loaded.State, terrain, new JournalSink(writer), new GameRules(), NullLogger<ServerSimulation>.Instance);
foreach (var entry in loaded.JournalTail)
{
    sim.Replay(entry);
}

var script = new WorldScript(seed);
var nextCheckpoint = sim.State.LastOpId.Value + CheckpointEvery;
Console.Out.WriteLine("READY");
Console.Out.Flush();
for (var step = 0; step < maxSteps; step++)
{
    script.Step(sim);
    while (writer.TryDequeueEvent(out var evt))
    {
        switch (evt)
        {
            case JournalCommitted committed:
                Console.Out.WriteLine($"DURABLE {committed.UpTo.Value.ToString(CultureInfo.InvariantCulture)}");
                Console.Out.Flush();
                break;
            case CheckpointCommitted done:
                sim.State.CompleteCheckpoint(done.Request.Core);
                break;
            case CheckpointFailed failed:
                sim.State.RequeueAfterFailedCheckpoint(failed.Request.Core);
                break;
            case WriterFaulted faulted:
                Console.Error.WriteLine($"FAULT {faulted.Error}");
                return 2;
            default:
                break;
        }
    }

    if (sim.State.LastOpId.Value >= nextCheckpoint && !sim.State.IsCheckpointInFlight)
    {
        var core = sim.State.CaptureCheckpoint();
        var blocks = new List<TerrainBlockPayload>(core.TerrainVersions.Count);
        foreach (var version in core.TerrainVersions)
        {
            blocks.Add(new TerrainBlockPayload(version.Key, version.Version, terrain.GetBlockData(version.Key)));
        }

        _ = writer.CheckpointAsync(new CheckpointRequest(core, blocks));
        nextCheckpoint = sim.State.LastOpId.Value + CheckpointEvery;
    }

    // Keep the journal queue near the writer's pace, like a 60 Hz server under load.
    if (step % 20 == 0)
    {
        Thread.Sleep(1);
    }
}

return 0;
