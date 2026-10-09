using GdUnit4;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

[TestSuite]
public class TerrainReplayTests
{
    private const float S = TerrainTestWorld.Surface;

    private static readonly Vector3 _area = TerrainTestWorld.Ground(0, 0);

    /// <summary>How the first session ends before the restart.</summary>
    public enum Ending
    {
        CloseWithCheckpoint,
        CrashAfterFlushWithoutCheckpoint,
        CheckpointAndTail,
        CrashInCheckpointTransaction,
    }

    private sealed record Live(ulong Digest, ulong StateHash, IReadOnlyList<TerrainBlockVersion> Versions);

    private static async Task Edits(TerrainTestWorld world, int seed, int count)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var center = new Vector3(random.Next(-20, 20) + (float)random.NextDouble(), S + random.Next(-2, 3), random.Next(-20, 20));
            world.Edit(center, 0.5f + ((float)random.NextDouble() * 2.5f), random.Next(2) == 0 ? TerrainEditMode.Add : TerrainEditMode.Remove);
            if (i % 10 == 9)
            {
                world.Sim.AdvanceTick();
                await world.WaitFrames(1);
            }
        }
    }

    private static Live Capture(TerrainTestWorld world) =>
        new(world.LiveDigest(), StateHasher.Hash(world.State), world.State.TerrainVersions.All.ToList());

    private static async Task AssertEqualsLive(TerrainTestWorld world, Live live)
    {
        world.AddViewer(_area, 96);
        AssertThat(await world.WaitLoaded(_area, 40)).IsTrue();
        AssertThat(world.State.TerrainVersions.All.ToList()).IsEqual(live.Versions);
        AssertThat(StateHasher.Hash(world.State)).IsEqual(live.StateHash);
        AssertThat(world.LiveDigest()).IsEqual(live.Digest);
        AssertThat(world.Node.Stream.ErrorCount).IsEqual(0);
    }

    [TestCase(Ending.CloseWithCheckpoint, Timeout = 600000)]
    [TestCase(Ending.CrashAfterFlushWithoutCheckpoint, Timeout = 600000)]
    [TestCase(Ending.CheckpointAndTail, Timeout = 600000)]
    [TestCase(Ending.CrashInCheckpointTransaction, Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Restart_CheckpointPlusTail_EqualsLive(Ending ending)
    {
        await using var scene = EngineTestScene.Create();
        var time = new FakeTimeProvider();
        var path = TerrainTestWorld.NewPath();
        Persistence.WorldCreator.Create(path, TerrainTestWorld.NewWorldState(), TerrainTestWorld.Options, time);
        var faults = ending == Ending.CrashInCheckpointTransaction ? new CrashFaults(FaultPoint.CheckpointMidTransaction, 2) : null;
        Live live;
        await using (var world = await TerrainTestWorld.OpenAsync(scene, path, faults: faults, time: time))
        {
            world.FaultsExpected = faults is not null;
            world.AddViewer(_area, 96);
            AssertThat(await world.WaitLoaded(_area, 40)).IsTrue();
            await Edits(world, 1, 40);
            switch (ending)
            {
                case Ending.CloseWithCheckpoint:
                    AssertThat(await world.CheckpointAndPump()).IsTrue();
                    break;
                case Ending.CrashAfterFlushWithoutCheckpoint:
                    AssertThat(await world.Writer.FlushAsync()).IsTrue();
                    break;
                case Ending.CheckpointAndTail:
                    AssertThat(await world.CheckpointAndPump()).IsTrue();
                    await Edits(world, 2, 30);
                    AssertThat(await world.Writer.FlushAsync()).IsTrue();
                    break;
                case Ending.CrashInCheckpointTransaction:
                    AssertThat(await world.CheckpointAndPump()).IsTrue();
                    await Edits(world, 3, 30);
                    AssertThat(await world.Writer.FlushAsync()).IsTrue();
                    AssertThat(await world.CheckpointAndPump()).IsFalse();
                    AssertThat(faults!.Crashed).IsTrue();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ending));
            }

            live = Capture(world);
        }

        await using var restarted = await TerrainTestWorld.OpenAsync(scene, path);
        AssertThat(restarted.TailCheck!.ReplayAllowed).IsTrue();
        await AssertEqualsLive(restarted, live);
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task FullReplay_FromEmptyCheckpoint_EqualsLive()
    {
        await using var scene = EngineTestScene.Create();
        Live live;
        string path;
        await using (var world = await TerrainTestWorld.CreateAsync(scene))
        {
            path = world.DbPath;
            world.AddViewer(_area, 96);
            AssertThat(await world.WaitLoaded(_area, 40)).IsTrue();
            await Edits(world, 4, 60);
            AssertThat(await world.Writer.FlushAsync()).IsTrue();
            live = Capture(world);
        }

        await using var replayed = await TerrainTestWorld.OpenAsync(scene, path);
        AssertThat(replayed.Loaded.LastCheckpointOpId).IsEqual(OpId.None);
        AssertThat(replayed.Loaded.JournalTail.Count).IsEqual(60);
        await AssertEqualsLive(replayed, live);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Replay_HostileEditFromDisk_RejectedBeforeLoading()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        var planet = WorldFactory.HomePlanet;
        var tooBig = new TerrainEditOp(planet, TerrainShape.Sphere, new Vector3(500, S, 500), TerrainRules.MaxEditRadiusMeters + 0.5f, TerrainEditMode.Remove, 0);
        var farAway = new TerrainEditOp(planet, TerrainShape.Sphere, new Vector3(0, 9000, 0), 1f, TerrainEditMode.Remove, 0);
        var unknown = tooBig with { Planet = new PlanetId(99), Radius = 1f };
        var loader = new TerrainAreaLoader(world.Service, world.Time, TimeSpan.FromSeconds(30));

        foreach (var hostile in new[] { tooBig, farAway, unknown })
        {
            IReadOnlyList<JournalEntry> tail = [new JournalEntry(new OpId(1), new Tick(1), null, hostile)];
            ReplayFailedException? error = null;
            try
            {
                await TerrainReplay.ReplayAsync(world.Sim, tail, loader, TerrainEditSemantics.Current, CancellationToken.None);
            }
            catch (ReplayFailedException exception)
            {
                error = exception;
            }

            AssertThat(error is not null).IsTrue();
            AssertThat(error!.OpId).IsEqual(new OpId(1));
        }

        AssertThat(loader.ViewersCreated).IsEqual(0);
        AssertThat(world.Sim.IsFaulted).IsFalse();
        AssertThat(world.State.LastOpId).IsEqual(OpId.None);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task AreaLoader_Deadline_ThrowsTimeout()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        var time = new FakeTimeProvider();
        var loader = new TerrainAreaLoader(world.Service, time, TimeSpan.FromSeconds(30));

        var loading = loader.EnsureLoadedAsync(WorldFactory.HomePlanet, new Vector3(2000, S, -2000), 7f, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(31));
        TimeoutException? error = null;
        try
        {
            await loading;
        }
        catch (TimeoutException exception)
        {
            error = exception;
        }

        AssertThat(error is not null).IsTrue();
        AssertThat(error!.Message).Contains("2000");
        AssertThat(loader.ViewersCreated).IsEqual(1);
        AssertThat(loader.ActiveViewers).IsEqual(0);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task AreaLoader_LoadsAreaAndReusesViewer()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        var loader = new TerrainAreaLoader(world.Service, world.Time, TimeSpan.FromSeconds(30));
        var first = new Vector3(-500, S, 300);

        await loader.EnsureLoadedAsync(WorldFactory.HomePlanet, first, 7f, CancellationToken.None);
        await loader.EnsureLoadedAsync(WorldFactory.HomePlanet, first + new Vector3(2, 0, 1), 7f, CancellationToken.None);
        await loader.EnsureLoadedAsync(WorldFactory.HomePlanet, new Vector3(600, S, -100), 7f, CancellationToken.None);

        AssertThat(world.Service.IsAreaEditable(WorldFactory.HomePlanet, new Vector3(600, S, -100), 7f)).IsTrue();
        AssertThat(loader.ViewersCreated).IsEqual(1);
        loader.ReleaseViewers();
        AssertThat(loader.ActiveViewers).IsEqual(0);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task LoadAreaBlocking_NotPreloaded_Throws()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = await TerrainTestWorld.CreateAsync(scene);
        var far = new Vector3(1500, S, 1500);

        AssertThrown(() => world.Service.LoadAreaBlocking(WorldFactory.HomePlanet, far, 5f))
            .IsInstanceOf<InvalidOperationException>()
            .HasMessage($"Terrain area of planet {WorldFactory.HomePlanet} around {far} (radius 5 m) not preloaded.");
        AssertThat(world.State.LastOpId).IsEqual(OpId.None);
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Replay_EditOverCorruptBlock_FailsFastWithoutRecord()
    {
        await using var scene = EngineTestScene.Create();
        var center = _area + new Vector3(3.5f, 0, -2.5f);
        string path;
        TerrainBlockKey broken;
        ulong checkpointHash;
        List<TerrainBlockVersion> checkpointVersions;
        await using (var world = await TerrainTestWorld.CreateAsync(scene))
        {
            path = world.DbPath;
            world.AddViewer(_area, 96);
            AssertThat(await world.WaitLoaded(_area, 40)).IsTrue();
            world.Edit(center, 2f, TerrainEditMode.Remove);
            AssertThat(await world.CheckpointAndPump()).IsTrue();
            checkpointHash = StateHasher.Hash(world.State);
            checkpointVersions = world.State.TerrainVersions.All.ToList();
            broken = checkpointVersions[0].Key;
            world.Edit(center + new Vector3(0.5f, 0, 0), 2f, TerrainEditMode.Add);
            AssertThat(await world.Writer.FlushAsync()).IsTrue();
        }

        // The saved data of the edited, checkpointed block is corrupt: the stream answers with an error and Voxel Tools puts an empty,
        // editable block there (S1). The tail edit over it must not run on that placeholder. (A deleted row would only drop the
        // version too: versions are read from the same rows.)
        AssertThat(TerrainTestWorld.CorruptSavedBlock(path, broken, [9, 9, 9])).IsEqual(1);
        await using var restarted = await TerrainTestWorld.OpenAsync(scene, path, replay: false);
        AssertThat(restarted.Loaded.JournalTail.Count).IsEqual(1);
        InvalidOperationException? error = null;
        try
        {
            await TerrainReplay.ReplayAsync(restarted.Sim, restarted.Loaded.JournalTail, restarted.Loader, TerrainEditSemantics.Current, CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            error = exception;
        }

        AssertThat(error is not null).IsTrue();
        AssertThat(error!.Message).Contains(broken.ToString());
        AssertThat(restarted.Service.FirstFault).IsEqual(broken);
        AssertThat(restarted.Blocks.PendingCount).IsEqual(0);
        AssertThat(restarted.Sim.IsFaulted).IsFalse();
        AssertThat(restarted.State.TerrainVersions.All.ToList()).IsEqual(checkpointVersions);
        AssertThat(StateHasher.Hash(restarted.State)).IsEqual(checkpointHash);
        AssertThat(restarted.Loader.ActiveViewers).IsEqual(0);
    }

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task Restart_DifferentSemanticsVersion_SafeModeWithoutReplay()
    {
        await using var scene = EngineTestScene.Create();
        string path;
        ulong checkpointHash;
        IReadOnlyList<TerrainBlockVersion> checkpointVersions;
        await using (var world = await TerrainTestWorld.CreateAsync(scene))
        {
            path = world.DbPath;
            world.AddViewer(_area, 96);
            AssertThat(await world.WaitLoaded(_area, 40)).IsTrue();
            await Edits(world, 7, 10);
            AssertThat(await world.CheckpointAndPump()).IsTrue();
            checkpointHash = StateHasher.Hash(world.State);
            checkpointVersions = world.State.TerrainVersions.All.ToList();
            await Edits(world, 8, 5);
            AssertThat(await world.Writer.FlushAsync()).IsTrue();
        }

        var saved = TerrainSemanticsMeta.Format(TerrainEditSemantics.SemanticsVersion + 1, TerrainEditSemantics.VoxelRef);
        await using var restarted = await TerrainTestWorld.OpenAsync(scene, path, savedSemantics: saved);
        var check = restarted.TailCheck!;

        AssertThat(check.IsMismatch).IsTrue();
        AssertThat(check.TerrainEditCount).IsEqual(5);
        AssertThat(check.FirstTerrainEditOpId).IsEqual(new OpId(11));
        AssertThat(check.Describe()).Contains(saved).Contains(TerrainEditSemantics.Current);
        AssertThat(restarted.Blocks.PendingCount).IsEqual(0);
        AssertThat(restarted.State.LastOpId).IsEqual(new OpId(10));
        AssertThat(StateHasher.Hash(restarted.State)).IsEqual(checkpointHash);
        AssertThat(restarted.State.TerrainVersions.All.ToList()).IsEqual(checkpointVersions);
    }
}
