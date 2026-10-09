using System.Numerics;


using Horyzont.Constructs.Meshing;
using Horyzont.Constructs.Sources;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Constructs;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Game.Tests.Constructs;

/// <summary>A world with a <see cref="ConstructWorld"/> over it, the way a solo or host session composes them.</summary>
internal sealed class ConstructTestWorld : IDisposable
{
    private readonly BlockVisuals? _visuals;

    public ConstructTestWorld(Godot.Node parent, WorldState state, ConstructWorldOptions? options = null, Action<MeshJobSlot>? beforeBuild = null)
    {
        Options = options ?? new ConstructWorldOptions();
        State = state;
        Catalog = BlockContentLoader.Load();
        Table = BlockRenderTable.Build(state.BlockTypes.Entries, Catalog);
        Source = new WorldStateConstructSource(state);
        Sim = new ServerSimulation(state, new NoTerrain(), new NoJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        _visuals = Options.Mode == ConstructWorldMode.CollisionOnly ? null : BlockVisuals.Create(Catalog);
        World = new ConstructWorld { Name = "ConstructWorld", BeforeBuild = beforeBuild };
        parent.AddChild(World);
        World.Initialize(Source, Table, _visuals, Options, NullLogger<ConstructWorld>.Instance);
        Visuals = _visuals;
    }

    public static FrameRef Planet => FrameRef.Of(WorldFactory.HomePlanet);

    public static FrameRef OtherPlanet => FrameRef.Of(new PlanetId(2));

    public ConstructWorldOptions Options { get; }

    public WorldState State { get; }

    public Horyzont.Constructs.Content.BlockTypeCatalog Catalog { get; }

    public BlockRenderTable Table { get; }

    public WorldStateConstructSource Source { get; }

    public ServerSimulation Sim { get; }

    public ConstructWorld World { get; }

    public BlockVisuals? Visuals { get; }

    /// <summary>A new world with the K2 block content and an empty planet.</summary>
    public static WorldState NewState(Horyzont.Constructs.Content.BlockTypeCatalog catalog) =>
        WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], catalog.BlockTypeKeys, ["stone", "dirt"]);

    /// <summary>A new world with the K2 block content and two empty planets (<see cref="Planet"/> and <see cref="OtherPlanet"/>).</summary>
    public static WorldState NewStateWithTwoPlanets(Horyzont.Constructs.Content.BlockTypeCatalog catalog) =>
        WorldState.CreateNew(
            WorldFactory.DefaultWorldId,
            42,
            [WorldFactory.HomePlanetParams, new PlanetParams(new PlanetId(2), new SystemId(1), 8, "core:test/2")],
            catalog.BlockTypeKeys,
            ["stone", "dirt"]);

    public ushort TypeId(string key)
    {
        State.BlockTypes.TryGetId(key, out var id);
        return id;
    }

    /// <summary>Applies an operation through the simulation and tells the construct world, like the composition root does.</summary>
    public AppliedOperation Apply(Operation operation)
    {
        var applied = Sim.ApplyServerOperation(operation);
        World.OnApplied(applied.Operation, applied.ConstructChanges);
        return applied;
    }

    public EntityId Create(CellPos cell, string blockKey = "core:stone_block", Vector3? origin = null, FrameRef? parent = null)
    {
        var id = State.EntityIds.PeekNext();
        Apply(new ConstructCreateOp(id, parent ?? Planet, origin ?? new Vector3(0, 4000, 0), Quaternion.Identity, cell, BlockState.Create(TypeId(blockKey), 0)));
        return id;
    }

    public void Set(EntityId construct, CellPos cell, string blockKey = "core:stone_block", byte orientation = 0) =>
        Apply(new BlockSetOp(construct, cell, BlockState.Create(TypeId(blockKey), orientation)));

    public void Clear(EntityId construct, CellPos cell) => Apply(new BlockClearOp(construct, cell));

    /// <summary>The mesher output for a chunk, computed on the calling thread from the current world state.</summary>
    public ChunkMeshData Expected(EntityId construct, ChunkPos chunk)
    {
        var input = new ChunkMeshInput();
        var data = new ChunkMeshData();
        input.Fill(Source, new Horyzont.Core.Checkpoints.ConstructChunkKey(construct, chunk), 1);
        new ChunkMesher().Build(input, Table, data);
        return data;
    }

    public void Dispose()
    {
        // The workers end on their own thread, so waiting here cannot deadlock the main thread.
        World.ShutdownAsync().GetAwaiter().GetResult();
        World.GetParent()?.RemoveChild(World);
        World.Free();
        _visuals?.Dispose();
    }

    private sealed class NoJournal : IJournalSink
    {
        public void Append(JournalEntry entry)
        {
        }
    }

    private sealed class NoTerrain : ITerrainService
    {
        public bool IsAreaLoaded(PlanetId planet, Vector3 center, float radius) => true;

        public void LoadAreaBlocking(PlanetId planet, Vector3 center, float radius)
        {
        }

        public bool Raycast(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
        {
            hit = default;
            return false;
        }

        public void ApplyEdit(TerrainEditOp edit)
        {
        }
    }
}
