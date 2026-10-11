using System.Runtime.ExceptionServices;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Persistence;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;
using Horyzont.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>
/// A server world for terrain engine tests: a world file, <see cref="ServerTerrainBlocks"/> over its store, the real
/// <see cref="VoxelTerrainService"/> with one planet whose terrain comes from the K2-08 factory with a flat generator at
/// <see cref="Surface"/> (the planet radius), the writer, the simulation and the checkpoint flow of the plan.
/// </summary>
internal sealed class TerrainTestWorld : IAsyncDisposable
{
    /// <summary>Height of the flat ground (y), equal to the planet radius so intents pass the core and atmosphere checks.</summary>
    public const float Surface = 4000f;

    private readonly EngineTestScene _scene;
    private readonly List<VoxelViewer> _viewers = [];
    private bool _disposed;

    private TerrainTestWorld(
        EngineTestScene scene, string path, FakeTimeProvider time, LoadedWorld loaded, TerrainBlockStore store, ServerTerrainBlocks blocks,
        VoxelTerrainService service, PlanetTerrain node, PersistenceWriter writer, ServerSimulation sim, TerrainAreaLoader loader)
    {
        _scene = scene;
        DbPath = path;
        Time = time;
        Loaded = loaded;
        Store = store;
        Blocks = blocks;
        Service = service;
        Node = node;
        Writer = writer;
        Sim = sim;
        Loader = loader;
    }

    public static PlanetParams Planet => new(WorldFactory.HomePlanet, new SystemId(1), 7, GeneratorProfiles.Current);

    public static PersistenceOptions Options => new() { GameVersion = "test-1", BackupInterval = Timeout.InfiniteTimeSpan };

    public string DbPath { get; }

    public FakeTimeProvider Time { get; }

    public LoadedWorld Loaded { get; }

    public TerrainBlockStore Store { get; }

    public ServerTerrainBlocks Blocks { get; }

    public VoxelTerrainService Service { get; }

    public PlanetTerrain Node { get; }

    public PersistenceWriter Writer { get; }

    public ServerSimulation Sim { get; }

    public TerrainAreaLoader Loader { get; }

    public TerrainTailCheck? TailCheck { get; private set; }

    public WorldState State => Sim.State;

    public TerrainBlockCodec Codec { get; } = new();

    public bool FaultsExpected { get; set; }

    public List<CheckpointRequest> Requests { get; } = [];

    public byte Stone
    {
        get
        {
            State.Materials.TryGetId(TerrainMaterials.StoneKey, out var id);
            return id;
        }
    }

    public byte Sand
    {
        get
        {
            State.Materials.TryGetId(TerrainMaterials.SandKey, out var id);
            return id;
        }
    }

    public static string NewPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "horyzont-k2-09-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "world.db");
    }

    public static WorldState NewWorldState(params PlanetParams[] morePlanets) =>
        WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [Planet, .. morePlanets], [WorldFactory.StoneBlockKey], TerrainMaterials.Keys);

    public static Task<TerrainTestWorld> CreateAsync(
        EngineTestScene scene, TerrainBufferOptions? options = null, IPersistenceFaults? faults = null, Func<VoxelGenerator>? generator = null,
        PlanetParams[]? morePlanets = null)
    {
        var time = new FakeTimeProvider();
        var path = NewPath();
        WorldCreator.Create(path, NewWorldState(morePlanets ?? []), Options, time);
        return OpenAsync(scene, path, options, faults, time, generator: generator);
    }

    /// <summary>Loads the world, builds the terrain and replays the tail through <see cref="TerrainReplay"/>.</summary>
    public static async Task<TerrainTestWorld> OpenAsync(
        EngineTestScene scene, string path, TerrainBufferOptions? options = null, IPersistenceFaults? faults = null, FakeTimeProvider? time = null,
        string? savedSemantics = null, bool replay = true, Func<VoxelGenerator>? generator = null)
    {
        time ??= new FakeTimeProvider();
        var loaded = WorldLoader.Load(
            path, new WorldLoadOptions { BackupsDirectory = Path.Combine(Path.GetDirectoryName(path)!, "backups") }, time, NullLogger.Instance);
        var store = new TerrainBlockStore(path);
        var blocks = new ServerTerrainBlocks(
            store, loaded.State.TerrainVersions.All, options ?? new TerrainBufferOptions(), time, NullLogger<ServerTerrainBlocks>.Instance);
        var service = new VoxelTerrainService(
            blocks, new TerrainBlockCodec(), TerrainMaterialMap.Build(loaded.State.Materials), NullLogger<VoxelTerrainService>.Instance);
        var node = scene.Add(service.AddPlanet(Planet, CreateTerrain(generator)));
        var writer = new PersistenceWriter(WorldDatabase.Open(path), Options, time, faults ?? NoFaults.Instance, NullLogger<PersistenceWriter>.Instance);
        var sim = new ServerSimulation(loaded.State, service, new JournalSink(writer), new GameRules(), NullLogger<ServerSimulation>.Instance);
        var loader = new TerrainAreaLoader(service, time, TerrainAreaLoader.DefaultDeadline);
        var world = new TerrainTestWorld(scene, path, time, loaded, store, blocks, service, node, writer, sim, loader);
        if (replay)
        {
            world.TailCheck = await TerrainReplay.ReplayAsync(sim, loaded.JournalTail, loader, savedSemantics ?? TerrainEditSemantics.Current, CancellationToken.None);
        }

        return world;
    }

    /// <summary>Fault injection: deletes the saved row of a block (<paramref name="data"/> null) or overwrites its data.</summary>
    /// <returns>Number of rows changed.</returns>
    public static int CorruptSavedBlock(string path, TerrainBlockKey key, byte[]? data)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = data is null
            ? "DELETE FROM terrain_blocks WHERE planet_id = $p AND lod = $lod AND block_x = $x AND block_y = $y AND block_z = $z;"
            : "UPDATE terrain_blocks SET data = $d WHERE planet_id = $p AND lod = $lod AND block_x = $x AND block_y = $y AND block_z = $z;";
        command.Parameters.AddWithValue("$p", (long)key.Planet.Value);
        command.Parameters.AddWithValue("$lod", (long)key.Lod);
        command.Parameters.AddWithValue("$x", (long)key.X);
        command.Parameters.AddWithValue("$y", (long)key.Y);
        command.Parameters.AddWithValue("$z", (long)key.Z);
        if (data is not null)
        {
            command.Parameters.AddWithValue("$d", data);
        }

        return command.ExecuteNonQuery();
    }

    /// <summary>The K2-08 factory terrain with a flat generator at <see cref="Surface"/>.</summary>
    public static VoxelLodTerrain CreateTerrain(Func<VoxelGenerator>? generator = null)
    {
        var terrain = PlanetTerrainFactory.Create(Planet, PlanetTerrainOptions.Server);
        terrain.Generator = generator?.Invoke() ?? new VoxelGeneratorFlat { Channel = TerrainChannels.Sdf, Height = Surface };
        return terrain;
    }

    public static Vector3 Ground(float x, float z) => new(x, Surface, z);

    /// <summary>Places a viewer (no visuals) at a planet frame position.</summary>
    public VoxelViewer AddViewer(Vector3 position, uint distance = 64)
    {
        var viewer = new VoxelViewer
        {
            Position = TerrainVectors.ToGodot(position),
            ViewDistance = distance,
            RequiresVisuals = false,
            RequiresCollisions = false,
        };
        Node.AddChild(viewer);
        _viewers.Add(viewer);
        return viewer;
    }

    public Task<bool> WaitLoaded(Vector3 center, float extent, int maxFrames = 3000) =>
        _scene.WaitFramesUntil(() => Service.IsAreaEditable(WorldFactory.HomePlanet, center, extent), maxFrames);

    public Task<bool> WaitUnloaded(Vector3 center, float extent, int maxFrames = 3000) =>
        _scene.WaitFramesUntil(() => !Service.IsAreaEditable(WorldFactory.HomePlanet, center, extent), maxFrames);

    public Task WaitFrames(int frames) => _scene.WaitFrames(frames);

    public TerrainEditOp EditOp(Vector3 center, float radius, TerrainEditMode mode) =>
        new(WorldFactory.HomePlanet, TerrainShape.Sphere, center, radius, mode, mode == TerrainEditMode.Add ? Stone : (byte)0);

    public AppliedOperation Edit(Vector3 center, float radius, TerrainEditMode mode) => Sim.ApplyServerOperation(EditOp(center, radius, mode));

    /// <summary>Digest of every edited block as Voxel Tools holds it now; every such block must be loaded.</summary>
    public ulong LiveDigest() => LiveDigest(State.TerrainVersions.All);

    /// <summary>
    /// Digest of the given blocks as Voxel Tools holds them now; a replica world passes the versions of the server, since the blocks it
    /// receives are pasted into its terrain and never enter its own state.
    /// </summary>
    public ulong LiveDigest(IEnumerable<TerrainBlockVersion> versions)
    {
        var entries = new List<(TerrainBlockKey, uint, ulong)>();
        using var buffer = new VoxelBuffer();
        foreach (var version in versions)
        {
            if (!Service.TryCopyBlock(version.Key, buffer))
            {
                throw new InvalidOperationException($"Block {version.Key} is not loaded.");
            }

            entries.Add((version.Key, version.Version, Codec.Digest(buffer)));
        }

        return TerrainDigest.Compute(entries);
    }

    /// <summary>Digest of the blocks of the given keys as the buffer and store serve them (version, decoded voxels).</summary>
    public ulong SourceDigest(IEnumerable<TerrainBlockKey> keys)
    {
        var entries = new List<(TerrainBlockKey, uint, ulong)>();
        using var buffer = new VoxelBuffer();
        foreach (var key in keys)
        {
            if (Blocks.TryLoad(key, out var version, out var blob) != TerrainBlockLookup.Found)
            {
                throw new InvalidOperationException($"Block {key} has no data.");
            }

            Codec.Decode(blob, buffer);
            entries.Add((key, version, Codec.Digest(buffer)));
        }

        return TerrainDigest.Compute(entries);
    }

    /// <summary>Captures a checkpoint, builds its request from the buffer and hands it to the writer.</summary>
    public Task<bool> RequestCheckpoint()
    {
        var core = State.CaptureCheckpoint();
        CheckpointRequest request;
        try
        {
            request = Blocks.CreateCheckpointRequest(core);
        }
        catch (InvalidOperationException)
        {
            State.RequeueAfterFailedCheckpoint(core);
            throw;
        }

        Requests.Add(request);
        return Writer.CheckpointAsync(request);
    }

    public async Task<bool> CheckpointAndPump()
    {
        var result = await RequestCheckpoint();
        Pump();
        return result;
    }

    /// <summary>The server loop's handling of writer events.</summary>
    public void Pump()
    {
        while (Writer.TryDequeueEvent(out var evt))
        {
            switch (evt)
            {
                case CheckpointCommitted committed:
                    State.CompleteCheckpoint(committed.Request.Core);
                    Blocks.OnCheckpointCommitted(committed.Request);
                    break;
                case CheckpointFailed failed:
                    State.RequeueAfterFailedCheckpoint(failed.Request.Core);
                    Blocks.OnCheckpointFailed(failed.Request);
                    break;
                case WriterFaulted faulted when !FaultsExpected:
                    ExceptionDispatchInfo.Throw(faulted.Error);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>The canonical shutdown of K2-10 for the terrain part: writer, terrain service, store only after a drained close.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Writer.DisposeAsync();
        Loader.ReleaseViewers();
        foreach (var viewer in _viewers)
        {
            if (GodotObject.IsInstanceValid(viewer))
            {
                viewer.QueueFree();
            }
        }

        var drained = await Service.ShutdownAsync();
        if (drained)
        {
            Store.Close();
        }

        Node.GetParent()?.RemoveChild(Node);
        Node.QueueFree();
        Service.Dispose();
        await _scene.WaitFrames(1);
    }
}
