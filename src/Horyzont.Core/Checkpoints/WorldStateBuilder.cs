using System.Numerics;
using System.Text;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Markers;
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
    private readonly List<MarkerRecord> _markers = [];
    private readonly List<ShipStateRecord> _shipStates = [];
    private StarSystemParams? _starSystem;
    private readonly List<RegistryEntry> _itemTypes = [];
    private readonly List<ContainerRecord> _containers = [];
    private OpId _lastOpId;
    private Tick _tick;
    private ulong _nextEntityId = 1;
    private ulong _nextPlayerId = 1;
    private ulong _nextMarkerId = 1;
    private ulong _nextContainerId = 1;
    private readonly List<BlockEntityRecord> _blockEntities = [];
    private readonly List<PlayerProgressRecord> _progress = [];
    private IReadOnlyList<string>? _progressKeys;
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

    /// <summary>Sets the container counter (<c>next_container_id</c>); 1 when never set.</summary>
    /// <param name="nextContainerId">Next container id.</param>
    public void SetNextContainerId(ulong nextContainerId) => _nextContainerId = nextContainerId;

    /// <summary>Adds an item type registry entry.</summary>
    /// <param name="entry">The entry.</param>
    public void AddItemType(RegistryEntry entry) => _itemTypes.Add(entry);

    /// <summary>Adds a container; its host player must be added too.</summary>
    /// <param name="container">The container.</param>
    public void AddContainer(ContainerRecord container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _containers.Add(container);
    }

    /// <summary>Adds the state of a functional block (K6-30); its block and, for a chest, its container must be added too. Checked in Build.</summary>
    /// <param name="record">The state.</param>
    public void AddBlockEntity(BlockEntityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _blockEntities.Add(record);
    }

    /// <summary>Adds the progress of a player (K6-30); the player must be added too. Checked in Build.</summary>
    /// <param name="record">The progress.</param>
    public void AddPlayerProgress(PlayerProgressRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _progress.Add(record);
    }

    /// <summary>Sets the progress keys of the world (K6-32); at most 256, 1 to 64 bytes each. Checked in Build.</summary>
    /// <param name="keys">The keys.</param>
    public void SetProgressKeys(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _progressKeys = keys;
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

    /// <summary>Adds a map marker (K5-27).</summary>
    /// <param name="marker">The marker.</param>
    public void AddMarker(MarkerRecord marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        _markers.Add(marker);
    }

    /// <summary>Sets the next marker id (K5-27); worlds without the value keep 1.</summary>
    /// <param name="nextMarkerId">Next marker id.</param>
    public void SetNextMarkerId(ulong nextMarkerId) => _nextMarkerId = nextMarkerId;

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
            state = new WorldState(_id, _galaxySeed, _lastOpId, _tick, _nextEntityId, _nextPlayerId, _nextMarkerId, _nextContainerId);
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

        foreach (var entry in _itemTypes)
        {
            state.ItemTypes.Add(entry);
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

        foreach (var container in _containers)
        {
            LoadContainer(state, container);
        }

        foreach (var record in _blockEntities)
        {
            LoadBlockEntity(state, record);
        }

        // A station container hosted by a block needs the station state that points at it (the other direction is checked in LoadBlockEntity).
        foreach (var kind in new[] { ContainerKind.StationInput, ContainerKind.StationOutput })
        {
            var stationContainers = state.Containers.OfKind(kind);
            for (var i = 0; i < stationContainers.Count; i++)
            {
                var container = stationContainers[i];
                if (container.Host.Kind != ContainerHostKind.ConstructBlock
                    || !(state.BlockEntities.TryGet(new BlockRef(new EntityId(container.Host.Id), container.Host.Cell), out var entity)
                        && entity is StationBlockState stationState
                        && (kind == ContainerKind.StationInput ? stationState.Input : stationState.Output) == container.Id))
                {
                    throw new InvalidDataException($"Station container {container.Id.Value} has no station state on its host block.");
                }
            }
        }

        // A chest container hosted by a block needs the matching chest state (the other direction is checked in LoadBlockEntity).
        var chests = state.Containers.OfKind(ContainerKind.Chest);
        for (var i = 0; i < chests.Count; i++)
        {
            var chest = chests[i];
            if (chest.Host.Kind == ContainerHostKind.ConstructBlock
                && !(state.BlockEntities.TryGet(new BlockRef(new EntityId(chest.Host.Id), chest.Host.Cell), out var entity)
                    && entity is ChestBlockState chestState && chestState.Container == chest.Id))
            {
                throw new InvalidDataException($"Chest container {chest.Id.Value} has no chest state on its host block.");
            }
        }

        foreach (var record in _progress)
        {
            LoadProgress(state, record);
        }

        LoadProgressKeys(state);

        foreach (var version in _terrain)
        {
            if (!state.Planets.TryGet(version.Key.Planet, out _))
            {
                throw new InvalidDataException($"Terrain version {version.Key} belongs to an unknown planet.");
            }

            state.TerrainVersions.Load(version);
        }

        foreach (var marker in _markers)
        {
            LoadMarker(state, marker);
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

    private static void LoadMarker(WorldState state, MarkerRecord marker)
    {
        if (marker.Id.IsNone || marker.Id.Value >= state.MarkerIds.NextValue || state.Markers.TryGet(marker.Id, out _))
        {
            throw new InvalidDataException($"Marker {marker.Id.Value} is duplicated or not below NextMarkerId.");
        }

        if (!state.Planets.TryGet(marker.Planet, out _) || !MarkerRules.IsValidName(marker.Name) || !MarkerRules.IsValidPosition(marker.Position)
            || !MarkerRules.IsValidStyle(marker.Icon, marker.Color))
        {
            throw new InvalidDataException($"Marker {marker.Id.Value} has invalid values.");
        }

        if (marker.Owner is { } owner)
        {
            if (!state.Players.TryGet(owner, out _) || state.Markers.CountPrivate(owner) >= MarkerRules.MaxPrivatePerPlayer)
            {
                throw new InvalidDataException($"Marker {marker.Id.Value} has an unknown owner or the owner is over the limit.");
            }
        }
        else if (state.Markers.CountShared >= MarkerRules.MaxShared)
        {
            throw new InvalidDataException($"Marker {marker.Id.Value} exceeds the shared marker limit.");
        }

        state.Markers.Load(marker);
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
            || !IsFinite(record.Position) || !Geometry.IsRotation(record.BodyRotation) || !float.IsFinite(record.Pitch)
            || record.Vitals is { IsValid: false })
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
            record.Position, record.BodyRotation, record.Pitch, record.IsOnline)
        {
            Vitals = record.Vitals,
        });
    }

    private static void LoadContainer(WorldState state, ContainerRecord record)
    {
        var id = record.Id;
        if (!state.ContainerIds.IsUsedId(id) || state.Containers.TryGet(id, out _))
        {
            throw new InvalidDataException($"Container {id.Value} is not below NextContainerId or is used twice.");
        }

        if (!Enum.IsDefined(record.Kind) || !Enum.IsDefined(record.Access) || record.Size == 0
            || record.Size > ProtocolConstants.MaxContainerSlots || record.Slots is null || record.Slots.Count != record.Size)
        {
            throw new InvalidDataException($"Container {id.Value} has an invalid kind, access or size.");
        }

        if (record.Host.Kind == ContainerHostKind.ConstructBlock)
        {
            var construct = new EntityId(record.Host.Id);
            if (record.Host != ContainerHost.ConstructBlock(construct, record.Host.Cell)
                || !state.Constructs.TryGet(construct, out var hostConstruct)
                || !record.Host.Cell.IsInBounds || hostConstruct.GetBlock(record.Host.Cell).IsEmpty
                || state.Containers.TryGetOfBlock(construct, record.Host.Cell, record.Kind, out _))
            {
                throw new InvalidDataException($"Container {id.Value} is hosted by a missing block or duplicates a container of its block.");
            }
        }
        else if (record.Host.Kind != ContainerHostKind.Player
            || !state.Players.TryGet(record.Host.PlayerId, out _)
            || state.Containers.TryGetOfPlayer(record.Host.PlayerId, record.Kind, out _))
        {
            throw new InvalidDataException($"Container {id.Value} has an unknown host or duplicates a container of its player.");
        }

        if (!record.Owner.IsNone && !state.Players.TryGet(record.Owner, out _))
        {
            throw new InvalidDataException($"Container {id.Value} has an unknown owner.");
        }

        foreach (var stack in record.Slots)
        {
            if ((stack.IsEmpty && stack.Count != 0) || (!stack.IsEmpty && (stack.Count == 0 || !state.ItemTypes.IsKnown(stack.Type))))
            {
                throw new InvalidDataException($"Container {id.Value} holds an invalid stack or an unknown item type {stack.Type.Value}.");
            }
        }

        var container = state.Containers.Add(id, record.Kind, record.Host, record.Owner, record.Access, record.Size, record.Version);
        for (var slot = 0; slot < record.Slots.Count; slot++)
        {
            container.SetSlot(slot, record.Slots[slot]);
        }
    }

    private static void LoadBlockEntity(WorldState state, BlockEntityRecord record)
    {
        var key = new BlockRef(record.Construct, record.Cell);
        if (!state.Constructs.TryGet(record.Construct, out var construct)
            || !record.Cell.IsInBounds || construct.GetBlock(record.Cell).IsEmpty || state.BlockEntities.TryGet(key, out _))
        {
            throw new InvalidDataException($"Block state of construct {record.Construct.Value} at {record.Cell} has no block or is duplicated.");
        }

        if (state.BlockEntities.CountOf(record.Construct) >= ProtocolConstants.MaxBlockEntitiesPerConstruct)
        {
            throw new InvalidDataException($"Construct {record.Construct.Value} has more than {ProtocolConstants.MaxBlockEntitiesPerConstruct} block states.");
        }

        switch (record.State)
        {
            case ChestBlockState chest:
                if (!state.Containers.TryGet(chest.Container, out var container) || container.Kind != ContainerKind.Chest
                    || container.Host != ContainerHost.ConstructBlock(record.Construct, record.Cell))
                {
                    throw new InvalidDataException($"The chest at {record.Cell} of construct {record.Construct.Value} has no chest container hosted by the block.");
                }

                break;
            case BedBlockState bed:
                if (!bed.Owner.IsNone && !state.Players.TryGet(bed.Owner, out _))
                {
                    throw new InvalidDataException($"The bed at {record.Cell} of construct {record.Construct.Value} has an unknown owner.");
                }

                break;
            case StationBlockState station:
                var host = ContainerHost.ConstructBlock(record.Construct, record.Cell);
                if (!state.Containers.TryGet(station.Input, out var input) || input.Kind != ContainerKind.StationInput || input.Host != host
                    || !state.Containers.TryGet(station.Output, out var output) || output.Kind != ContainerKind.StationOutput || output.Host != host)
                {
                    throw new InvalidDataException($"The station at {record.Cell} of construct {record.Construct.Value} has no input and output containers hosted by the block.");
                }

                if (!StrictUtf8.TryGetByteCount(station.Recipe, out var recipeBytes) || recipeBytes > ProtocolConstants.MaxRecipeKeyBytes
                    || (station.Started is not null && recipeBytes == 0))
                {
                    throw new InvalidDataException($"The station at {record.Cell} of construct {record.Construct.Value} has an invalid recipe.");
                }

                if (state.BlockEntities.StationsOf(record.Construct) >= ProtocolConstants.MaxTimedStationsPerConstruct)
                {
                    throw new InvalidDataException($"Construct {record.Construct.Value} has more than {ProtocolConstants.MaxTimedStationsPerConstruct} stations with time.");
                }

                break;
            default:
                throw new InvalidDataException($"Block state kind {record.State.Kind} is not supported.");
        }

        state.BlockEntities.Load(key, record.State);
    }

    private static void LoadProgress(WorldState state, PlayerProgressRecord record)
    {
        // A record exists if and only if a field differs from its default: a respawn point, a discovery or food (K6-32 D7, K6-33 D4).
        if (!state.Players.TryGet(record.Player, out _)
            || (record.RespawnBed is null && record.Discovered is null && record.Food.IsEmpty)
            || state.Progress.TryGet(record.Player, out _))
        {
            throw new InvalidDataException($"Progress of player {record.Player.Value} is for an unknown player, is empty or is duplicated.");
        }

        if (record.Discovered is { } discovered)
        {
            for (var type = 1; type <= ProtocolConstants.MaxItemTypes; type++)
            {
                if (discovered.Contains(new ItemTypeId((ushort)type)) && !state.ItemTypes.IsKnown(new ItemTypeId((ushort)type)))
                {
                    throw new InvalidDataException($"Player {record.Player.Value} discovered the unknown item type {type}.");
                }
            }
        }

        CheckFood(state, record);
        state.Progress.Load(new PlayerProgress(record.Player, record.RespawnBed, record.Discovered, record.Food));
    }

    /// <summary>Food slots of a record: an empty slot has tick 0, a filled one a type known to the world, and no type repeats (K6-33, D6).</summary>
    private static void CheckFood(WorldState state, PlayerProgressRecord record)
    {
        for (var i = 0; i < FoodSlots.Count; i++)
        {
            var slot = record.Food[i];
            if (slot.IsEmpty)
            {
                if (slot.Eaten.Value != 0)
                {
                    throw new InvalidDataException($"Player {record.Player.Value} has an empty food slot with a tick.");
                }

                continue;
            }

            if (slot.Type.Value > ProtocolConstants.MaxItemTypes || !state.ItemTypes.IsKnown(slot.Type))
            {
                throw new InvalidDataException($"Player {record.Player.Value} eats the unknown item type {slot.Type.Value}.");
            }

            for (var j = 0; j < i; j++)
            {
                if (record.Food[j].Type == slot.Type)
                {
                    throw new InvalidDataException($"Player {record.Player.Value} has the food type {slot.Type.Value} twice.");
                }
            }
        }
    }

    private void LoadProgressKeys(WorldState state)
    {
        if (_progressKeys is not { } keys)
        {
            return;
        }

        if (keys.Count > ProtocolConstants.MaxProgressKeys)
        {
            throw new InvalidDataException($"The world has {keys.Count} progress keys, the most is {ProtocolConstants.MaxProgressKeys}.");
        }

        for (var i = 0; i < keys.Count; i++)
        {
            if (!StrictUtf8.TryGetByteCount(keys[i], out var bytes) || bytes == 0 || bytes > ProtocolConstants.MaxRegistryKeyBytes)
            {
                throw new InvalidDataException("A progress key is empty, longer than 64 bytes or not valid UTF-8.");
            }

            if (i > 0 && string.CompareOrdinal(keys[i - 1], keys[i]) >= 0)
            {
                throw new InvalidDataException("Progress keys must be strictly ascending in ordinal order.");
            }

            state.ProgressKeys.Load(keys[i]);
        }
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
