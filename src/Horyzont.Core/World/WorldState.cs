using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.World;

/// <summary>
/// Canonical state of a world. Mutated only by <see cref="Simulation.ServerSimulation"/> through operations
/// and built by <see cref="WorldStateBuilder"/> when a world is loaded.
/// </summary>
public sealed class WorldState
{
    private static readonly Comparison<ConstructChunkKey> _chunkKeyOrder = static (a, b) =>
    {
        var construct = a.Construct.Value.CompareTo(b.Construct.Value);
        return construct != 0 ? construct : a.Chunk.CompareTo(b.Chunk);
    };

    private readonly ChangeTracker _tracker = new();
    private WorldCheckpoint? _inFlight;

    internal WorldState(WorldId id, ulong galaxySeed, OpId lastOpId, Tick tick, ulong nextEntityId, ulong nextPlayerId)
    {
        Id = id;
        GalaxySeed = galaxySeed;
        LastOpId = lastOpId;
        CurrentTick = tick;
        EntityIds = new EntityIdAllocator(nextEntityId);
        PlayerIds = new PlayerIdAllocator(nextPlayerId);
        Planets = new PlanetRegistry();
        BlockTypes = new BlockTypeRegistry();
        Materials = new MaterialRegistry();
        Constructs = new ConstructStore(_tracker);
        Players = new PlayerStore(_tracker);
        TerrainVersions = new TerrainVersionTable(_tracker);
    }

    /// <summary>World id.</summary>
    public WorldId Id { get; }

    /// <summary>Galaxy seed.</summary>
    public ulong GalaxySeed { get; }

    /// <summary>Last applied operation.</summary>
    public OpId LastOpId { get; private set; }

    /// <summary>Current simulation tick.</summary>
    public Tick CurrentTick { get; private set; }

    /// <summary>Persistent entity ids.</summary>
    public EntityIdAllocator EntityIds { get; }

    /// <summary>Player ids.</summary>
    public PlayerIdAllocator PlayerIds { get; }

    /// <summary>Planets.</summary>
    public PlanetRegistry Planets { get; }

    /// <summary>Block types.</summary>
    public BlockTypeRegistry BlockTypes { get; }

    /// <summary>Terrain materials.</summary>
    public MaterialRegistry Materials { get; }

    /// <summary>Constructs.</summary>
    public ConstructStore Constructs { get; }

    /// <summary>Players.</summary>
    public PlayerStore Players { get; }

    /// <summary>Versions of edited terrain blocks.</summary>
    public TerrainVersionTable TerrainVersions { get; }

    /// <summary>True between <see cref="CaptureCheckpoint"/> and its completion or requeue.</summary>
    public bool IsCheckpointInFlight => _inFlight is not null;

    internal ChangeTracker Tracker => _tracker;

    /// <summary>
    /// Creates a new world. Block types get numbers from 1 in the given order; <see cref="BlockTypeRegistry.MissingKey"/>
    /// is appended when absent. Materials get numbers from 1 in the given order.
    /// LastOpId = None, CurrentTick = 0, NextEntityId = 1, NextPlayerId = 1.
    /// </summary>
    /// <param name="id">World id.</param>
    /// <param name="galaxySeed">Galaxy seed.</param>
    /// <param name="planets">Planets.</param>
    /// <param name="blockTypeKeys">Block type keys.</param>
    /// <param name="materialKeys">Material keys.</param>
    /// <returns>The new world.</returns>
    public static WorldState CreateNew(
        WorldId id, ulong galaxySeed, IEnumerable<PlanetParams> planets, IEnumerable<string> blockTypeKeys,
        IEnumerable<string> materialKeys)
    {
        ArgumentNullException.ThrowIfNull(planets);
        ArgumentNullException.ThrowIfNull(blockTypeKeys);
        ArgumentNullException.ThrowIfNull(materialKeys);
        var state = new WorldState(id, galaxySeed, OpId.None, default, 1, 1);
        foreach (var planet in planets)
        {
            state.Planets.Add(planet);
        }

        foreach (var key in blockTypeKeys)
        {
            if (state.BlockTypes.TryGetId(key, out _))
            {
                throw new ArgumentException($"Duplicate block type key '{key}'.", nameof(blockTypeKeys));
            }

            state.BlockTypes.GetOrAssign(key);
        }

        if (!state.BlockTypes.TryGetId(BlockTypeRegistry.MissingKey, out _))
        {
            state.BlockTypes.GetOrAssign(BlockTypeRegistry.MissingKey);
        }

        foreach (var key in materialKeys)
        {
            if (state.Materials.TryGetId(key, out _))
            {
                throw new ArgumentException($"Duplicate material key '{key}'.", nameof(materialKeys));
            }

            state.Materials.GetOrAssign(key);
        }

        return state;
    }

    /// <summary>True when the frame exists: a registered planet, an existing construct or system 1.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>Whether it exists.</returns>
    public bool FrameExists(FrameRef frame) => frame.Kind switch
    {
        FrameKind.System => frame.Id == 1,
        FrameKind.Planet => Planets.TryGet(new PlanetId(frame.Id), out _),
        FrameKind.Construct => Constructs.TryGet(new EntityId(frame.Id), out _),
        _ => false,
    };

    /// <summary>
    /// Planet that contains a frame: the planet frame itself or, for a construct, the planet at the end of its parent chain.
    /// False for the system frame and for unknown frames.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="planet">The planet when resolved.</param>
    /// <returns>Whether the frame lies in a planet.</returns>
    public bool TryResolvePlanet(FrameRef frame, out PlanetId planet)
    {
        planet = default;
        for (var depth = 0; depth <= Constructs.Count; depth++)
        {
            switch (frame.Kind)
            {
                case FrameKind.Planet:
                    planet = new PlanetId(frame.Id);
                    return Planets.TryGet(planet, out _);
                case FrameKind.Construct when Constructs.TryGet(new EntityId(frame.Id), out var construct):
                    frame = construct.Parent;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Converts a point of a frame into the frame of the planet that contains it.
    /// False when the frame does not lie in a planet.
    /// </summary>
    /// <param name="frame">Frame of the point.</param>
    /// <param name="point">Point in <paramref name="frame"/>.</param>
    /// <param name="planet">Planet of the frame.</param>
    /// <param name="planetPoint">The point in the planet frame.</param>
    /// <returns>Whether the conversion succeeded.</returns>
    public bool TryToPlanetFrame(FrameRef frame, Vector3 point, out PlanetId planet, out Vector3 planetPoint)
    {
        planet = default;
        planetPoint = point;
        for (var depth = 0; depth <= Constructs.Count; depth++)
        {
            switch (frame.Kind)
            {
                case FrameKind.Planet:
                    planet = new PlanetId(frame.Id);
                    return Planets.TryGet(planet, out _);
                case FrameKind.Construct when Constructs.TryGet(new EntityId(frame.Id), out var construct):
                    planetPoint = construct.LocalToParent(planetPoint);
                    frame = construct.Parent;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Captures the changes since the previous checkpoint (all online players are always included) and clears the change
    /// marks. Called on a tick boundary. At most one checkpoint can be in flight.
    /// </summary>
    /// <returns>The checkpoint to write.</returns>
    /// <exception cref="InvalidOperationException">A checkpoint is already in flight.</exception>
    public WorldCheckpoint CaptureCheckpoint()
    {
        if (_inFlight is not null)
        {
            throw new InvalidOperationException("A checkpoint is already in flight.");
        }

        var constructIds = _tracker.Constructs.ToArray();
        Array.Sort(constructIds, static (a, b) => a.Value.CompareTo(b.Value));
        var upsertedConstructs = new List<ConstructHeaderRecord>();
        var removedConstructs = new List<EntityId>();
        foreach (var id in constructIds)
        {
            if (Constructs.TryGet(id, out var construct))
            {
                upsertedConstructs.Add(ConstructHeaderRecord.From(construct));
            }
            else
            {
                removedConstructs.Add(id);
            }
        }

        var chunkKeys = _tracker.Chunks.ToArray();
        Array.Sort(chunkKeys, _chunkKeyOrder);
        var upsertedChunks = new List<ConstructChunkRecord>();
        var removedChunks = new List<ConstructChunkKey>();
        foreach (var key in chunkKeys)
        {
            if (Constructs.TryGet(key.Construct, out var construct) && construct.TryGetChunk(key.Chunk, out var chunk) && chunk is not null)
            {
                upsertedChunks.Add(new ConstructChunkRecord(key, chunk.ToArray()));
            }
            else
            {
                removedChunks.Add(key);
            }
        }

        var players = new List<PlayerRecord>();
        foreach (var player in Players.All)
        {
            if (player.IsOnline || _tracker.Players.Contains(player.Player))
            {
                players.Add(PlayerRecord.From(player));
            }
        }

        var terrainKeys = _tracker.Terrain.ToArray();
        Array.Sort(terrainKeys);
        var terrain = new List<TerrainBlockVersion>(terrainKeys.Length);
        foreach (var key in terrainKeys)
        {
            terrain.Add(new TerrainBlockVersion(key, TerrainVersions.Get(key)));
        }

        _tracker.Clear();
        _inFlight = new WorldCheckpoint(
            LastOpId, CurrentTick, EntityIds.NextValue, PlayerIds.NextValue,
            upsertedConstructs, removedConstructs, upsertedChunks, removedChunks, players, terrain);
        return _inFlight;
    }

    /// <summary>Closes the checkpoint in flight after it was written.</summary>
    /// <param name="committed">The checkpoint returned by <see cref="CaptureCheckpoint"/>.</param>
    public void CompleteCheckpoint(WorldCheckpoint committed)
    {
        EnsureInFlight(committed);
        _inFlight = null;
    }

    /// <summary>
    /// Closes the checkpoint in flight after a failed write and marks all its keys again, so the next checkpoint writes
    /// their current state or their removal.
    /// </summary>
    /// <param name="failed">The checkpoint returned by <see cref="CaptureCheckpoint"/>.</param>
    public void RequeueAfterFailedCheckpoint(WorldCheckpoint failed)
    {
        EnsureInFlight(failed);
        foreach (var header in failed.UpsertedConstructs)
        {
            _tracker.MarkConstruct(header.Id);
        }

        foreach (var id in failed.RemovedConstructs)
        {
            _tracker.MarkConstruct(id);
        }

        foreach (var chunk in failed.UpsertedChunks)
        {
            _tracker.MarkChunk(chunk.Key);
        }

        foreach (var key in failed.RemovedChunks)
        {
            _tracker.MarkChunk(key);
        }

        foreach (var player in failed.UpsertedPlayers)
        {
            _tracker.MarkPlayer(player.Player);
        }

        foreach (var version in failed.TerrainVersions)
        {
            _tracker.MarkTerrain(version.Key);
        }

        _inFlight = null;
    }

    /// <summary>True when a player or a construct has the frame as its parent.</summary>
    internal bool HasChildren(FrameRef frame)
    {
        var players = Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i].Frame == frame)
            {
                return true;
            }
        }

        var constructs = Constructs.All;
        for (var i = 0; i < constructs.Count; i++)
        {
            if (constructs[i].Parent == frame)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a player, online or offline, sits in the cell of the construct.</summary>
    internal bool TryGetSeated(EntityId construct, CellPos cell, [NotNullWhen(true)] out PlayerState? seated)
    {
        var frame = FrameRef.Of(construct);
        var players = Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.Frame == frame && player.Seat == cell)
            {
                seated = player;
                return true;
            }
        }

        seated = null;
        return false;
    }

    internal void SetLastOpId(OpId id) => LastOpId = id;

    internal void SetTick(Tick tick) => CurrentTick = tick;

    private void EnsureInFlight(WorldCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!ReferenceEquals(checkpoint, _inFlight))
        {
            throw new InvalidOperationException("The checkpoint is not the one in flight.");
        }
    }
}
