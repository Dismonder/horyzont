using System.Numerics;
using System.Text;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Checkpoints;

/// <summary>Builds a <see cref="WorldState"/> when a world is loaded (K1-05). Marks nothing as changed.</summary>
public sealed class WorldStateBuilder
{
    private readonly WorldId _id;
    private readonly ulong _galaxySeed;
    private readonly List<PlanetParams> _planets = [];
    private readonly List<RegistryEntry> _blockTypes = [];
    private readonly List<RegistryEntry> _materials = [];
    private readonly List<ConstructHeaderRecord> _constructs = [];
    private readonly HashSet<EntityId> _constructIds = [];
    private readonly List<ConstructChunkRecord> _chunks = [];
    private readonly List<PlayerRecord> _players = [];
    private readonly List<TerrainBlockVersion> _terrain = [];
    private readonly List<ShipStateRecord> _shipStates = [];
    private StarSystemParams? _starSystem;
    private OpId _lastOpId;
    private Tick _tick;
    private ulong _nextEntityId = 1;
    private ulong _nextPlayerId = 1;
    private bool _built;

    /// <summary>Starts a world.</summary>
    /// <param name="id">World id.</param>
    /// <param name="galaxySeed">Galaxy seed.</param>
    public WorldStateBuilder(WorldId id, ulong galaxySeed)
    {
        _id = id;
        _galaxySeed = galaxySeed;
    }

    /// <summary>Sets the barrier and tick of the last checkpoint and the allocators.</summary>
    /// <param name="lastOpId">Last operation covered by the checkpoints.</param>
    /// <param name="tick">Tick of the last checkpoint.</param>
    /// <param name="nextEntityId">Next persistent entity id.</param>
    /// <param name="nextPlayerId">Next player id.</param>
    public void SetProgress(OpId lastOpId, Tick tick, ulong nextEntityId, ulong nextPlayerId)
    {
        _lastOpId = lastOpId;
        _tick = tick;
        _nextEntityId = nextEntityId;
        _nextPlayerId = nextPlayerId;
    }

    /// <summary>Adds a planet.</summary>
    /// <param name="planet">The planet.</param>
    public void AddPlanet(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        _planets.Add(planet);
    }

    /// <summary>Sets the star system (K5-21); checked against the planets in <see cref="Build"/>.</summary>
    /// <param name="system">The star system.</param>
    public void SetStarSystem(StarSystemParams system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (_starSystem is not null)
        {
            throw new InvalidDataException("Duplicate star system.");
        }

        _starSystem = system;
    }

    /// <summary>Adds a block type registry entry.</summary>
    /// <param name="entry">The entry.</param>
    public void AddBlockType(RegistryEntry entry) => _blockTypes.Add(entry);

    /// <summary>Adds a material registry entry.</summary>
    /// <param name="entry">The entry.</param>
    public void AddMaterial(RegistryEntry entry) => _materials.Add(entry);

    /// <summary>Adds a construct header.</summary>
    /// <param name="header">The header.</param>
    public void AddConstruct(ConstructHeaderRecord header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (!_constructIds.Add(header.Id))
        {
            throw new InvalidDataException($"Duplicate construct {header.Id.Value}.");
        }

        _constructs.Add(header);
    }

    /// <summary>Adds a chunk of a construct that was already added.</summary>
    /// <param name="chunk">The chunk.</param>
    public void AddChunk(ConstructChunkRecord chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (!_constructIds.Contains(chunk.Key.Construct))
        {
            throw new InvalidDataException($"Chunk {chunk.Key.Chunk} belongs to unknown construct {chunk.Key.Construct.Value}.");
        }

        _chunks.Add(chunk);
    }

    /// <summary>Adds a player.</summary>
    /// <param name="player">The player.</param>
    public void AddPlayer(PlayerRecord player)
    {
        ArgumentNullException.ThrowIfNull(player);
        _players.Add(player);
    }

    /// <summary>Adds a terrain block version.</summary>
    /// <param name="version">The version.</param>
    public void AddTerrainVersion(TerrainBlockVersion version) => _terrain.Add(version);

    /// <summary>Adds the flight state of a ship (K5-24); the construct must exist, the record must be valid.</summary>
    /// <param name="record">The record.</param>
    public void AddShipState(ShipStateRecord record) => _shipStates.Add(record);

    /// <summary>Builds the world.</summary>
    /// <returns>The world state.</returns>
    /// <exception cref="InvalidDataException">The data is inconsistent.</exception>
    public WorldState Build()
    {
        if (_built)
        {
            throw new InvalidOperationException("The builder was already used.");
        }

        _built = true;
        WorldState state;
        try
        {
            state = new WorldState(_id, _galaxySeed, _lastOpId, _tick, _nextEntityId, _nextPlayerId);
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new InvalidDataException("Allocator values out of range.", e);
        }

        foreach (var planet in _planets)
        {
            state.Planets.Add(planet);
        }

        if (_starSystem is not null)
        {
            state.SetStarSystem(_starSystem);
        }

        foreach (var entry in _blockTypes)
        {
            state.BlockTypes.Add(entry);
        }

        foreach (var entry in _materials)
        {
            state.Materials.Add(entry);
        }

        foreach (var header in _constructs)
        {
            CheckEntity(state, header.Id);
            if (!Enum.IsDefined(header.Mode) || !IsFinite(header.Origin) || !Geometry.IsRotation(header.Rotation))
            {
                throw new InvalidDataException($"Construct {header.Id.Value} has an invalid header.");
            }

            state.Constructs.Load(header.Id, header.Parent, header.ParentSeq, header.Mode, header.Origin, header.Rotation, header.Version);
        }

        foreach (var chunk in _chunks)
        {
            if (!IsChunkInRange(chunk.Key.Chunk))
            {
                throw new InvalidDataException($"Construct {chunk.Key.Construct.Value} has chunk {chunk.Key.Chunk} outside the cell range.");
            }

            foreach (var value in chunk.Blocks)
            {
                var block = new BlockState(value);
                if (!block.IsEmpty && !state.BlockTypes.IsKnown(block.TypeId))
                {
                    throw new InvalidDataException($"Construct {chunk.Key.Construct.Value} uses unknown block type {block.TypeId}.");
                }
            }

            state.Constructs.TryGet(chunk.Key.Construct, out var construct);
            construct!.LoadChunk(chunk.Key.Chunk, chunk.Blocks);
        }

        foreach (var construct in state.Constructs.All)
        {
            if (construct.BlockCount == 0)
            {
                throw new InvalidDataException($"Construct {construct.Id.Value} has no blocks.");
            }

            if (!state.FrameExists(construct.Parent) || (construct.Parent.Kind == FrameKind.Construct && !ReachesRoot(state, construct)))
            {
                throw new InvalidDataException($"Construct {construct.Id.Value} has a missing or cyclic parent frame.");
            }
        }

        foreach (var record in _players)
        {
            LoadPlayer(state, record);
        }

        foreach (var version in _terrain)
        {
            if (!state.Planets.TryGet(version.Key.Planet, out _))
            {
                throw new InvalidDataException($"Terrain version {version.Key} belongs to an unknown planet.");
            }

            state.TerrainVersions.Load(version);
        }

        foreach (var ship in _shipStates)
        {
            if (!ship.IsValid || !state.Constructs.TryGet(ship.Construct, out _) || state.ShipStates.TryGet(ship.Construct, out _))
            {
                throw new InvalidDataException($"Ship state of construct {ship.Construct.Value} is invalid, duplicate or has no construct.");
            }

            state.ShipStates.Set(ship);
        }

        return state;
    }

    /// <summary>True when every cell of the chunk is in <see cref="CellPos.IsInBounds"/> (no shift, so no overflow).</summary>
    private static bool IsChunkInRange(ChunkPos chunk)
    {
        const int Limit = CellPos.Limit / ConstructChunk.Size;
        return chunk.X >= -Limit && chunk.X < Limit && chunk.Y >= -Limit && chunk.Y < Limit && chunk.Z >= -Limit && chunk.Z < Limit;
    }

    private static void LoadPlayer(WorldState state, PlayerRecord record)
    {
        if (record.Player.IsNone || record.Player.Value >= state.PlayerIds.NextValue)
        {
            throw new InvalidDataException($"Player {record.Player.Value} is not below NextPlayerId.");
        }

        CheckEntity(state, record.Entity);
        if (string.IsNullOrEmpty(record.DisplayName)
            || Encoding.UTF8.GetByteCount(record.DisplayName) > ProtocolConstants.MaxDisplayNameBytes
            || !IsFinite(record.Position) || !Geometry.IsRotation(record.BodyRotation) || !float.IsFinite(record.Pitch))
        {
            throw new InvalidDataException($"Player {record.Player.Value} has invalid values.");
        }

        if (!state.FrameExists(record.Frame))
        {
            throw new InvalidDataException($"Player {record.Player.Value} is in a missing frame.");
        }

        if (record.Seat is { } seat
            && (record.Frame.Kind != FrameKind.Construct
                || !state.Constructs.TryGet(new EntityId(record.Frame.Id), out var seatConstruct)
                || seatConstruct.GetBlock(seat).IsEmpty
                || state.TryGetSeated(seatConstruct.Id, seat, out _)))
        {
            throw new InvalidDataException($"Player {record.Player.Value} has an invalid seat.");
        }

        state.Players.Load(new PlayerState(
            record.Player, record.Identity, record.DisplayName, record.Entity, record.Frame, record.ParentSeq, record.Seat,
            record.Position, record.BodyRotation, record.Pitch, record.IsOnline));
    }

    private static void CheckEntity(WorldState state, EntityId id)
    {
        if (id.IsNone || id.IsTransient || id.Value >= state.EntityIds.NextValue)
        {
            throw new InvalidDataException($"Entity {id.Value} is not a used persistent id.");
        }

        if (state.Constructs.TryGet(id, out _) || state.Players.TryGetByEntity(id, out _))
        {
            throw new InvalidDataException($"Entity {id.Value} is used twice.");
        }
    }

    private static bool ReachesRoot(WorldState state, Construct construct)
    {
        var frame = construct.Parent;
        for (var depth = 0; depth <= state.Constructs.Count; depth++)
        {
            if (frame.Kind != FrameKind.Construct)
            {
                return true;
            }

            if (!state.Constructs.TryGet(new EntityId(frame.Id), out var parent))
            {
                return false;
            }

            frame = parent.Parent;
        }

        return false;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
