using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Markers;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>The largest valid message of every type: full lists, longest strings, maximal varuint values, worst-case run-length encoding.</summary>
public static class MaxMessages
{
    /// <summary>A string of exactly <paramref name="bytes"/> UTF-8 bytes that mixes 1-byte and 3-byte characters.</summary>
    /// <param name="bytes">Encoded length.</param>
    /// <returns>The string.</returns>
    public static string Text(int bytes) => new string('€', bytes / 3) + new string('a', bytes % 3);

    /// <summary>A slot with the largest encoded stack.</summary>
    /// <param name="slot">Slot index.</param>
    /// <returns>The slot content.</returns>
    public static SlotContent MaxSlot(int slot) =>
        new((ushort)slot, new ItemStack(new ItemTypeId(ProtocolConstants.MaxItemTypes), ushort.MaxValue));

    /// <summary>The worst-case snapshot: 8 frames with maximal ids and 22 entities with every field present and maximal varuints.</summary>
    /// <returns>The snapshot.</returns>
    public static Snapshot Snapshot()
    {
        var frames = Enumerable.Range(0, ProtocolConstants.MaxFramesPerSnapshot)
            .Select(i => new SnapshotFrame(new FrameRef(FrameKind.Construct, ulong.MaxValue - (ulong)i), uint.MaxValue))
            .ToArray();
        var entities = Enumerable.Range(0, ProtocolConstants.MaxEntitiesPerSnapshot)
            .Select(i => FullState(ulong.MaxValue - (ulong)i, (byte)(frames.Length - 1)))
            .ToArray();
        return new Snapshot(uint.MaxValue, new Tick(ulong.MaxValue), uint.MaxValue, uint.MaxValue, frames, entities);
    }

    /// <summary>An entity state with every field present and maximal values.</summary>
    /// <param name="entity">Entity id.</param>
    /// <param name="frameIndex">Frame index.</param>
    /// <returns>The state.</returns>
    public static EntityState FullState(ulong entity, byte frameIndex) =>
        new(
            new EntityId(entity),
            EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.Velocity | EntityStateFields.MotionFlags,
            frameIndex,
            uint.MaxValue,
            new Vector3(float.MaxValue, float.MinValue, float.MaxValue),
            Quaternion.Normalize(new Quaternion(0.5f, -0.5f, 0.5f, 0.5f)),
            new Vector3(65504f, -65504f, 65504f),
            byte.MaxValue);

    /// <summary>The largest chunk for run-length encoding: neighbours always differ, so there are 4096 runs.</summary>
    /// <param name="x">Chunk X coordinate.</param>
    /// <returns>The chunk record.</returns>
    public static ConstructChunkRecord WorstChunk(int x)
    {
        var blocks = new uint[4096];
        var a = BlockState.Create(ushort.MaxValue, 23, 15, 7).Packed;
        var b = BlockState.Create(ushort.MaxValue - 1, 23, 15, 7).Packed;
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = (i & 1) == 0 ? a : b;
        }

        return new ConstructChunkRecord(new ConstructChunkKey(new EntityId(ulong.MaxValue), new ChunkPos(int.MinValue + x, int.MinValue, int.MinValue)), blocks);
    }

    /// <summary>Every message type at its maximal size.</summary>
    /// <returns>One message per <see cref="MessageType"/>.</returns>
    public static IReadOnlyList<Message> All()
    {
        var maxEntity = new EntityId(ulong.MaxValue);
        var maxFrame = new FrameRef(FrameKind.Construct, ulong.MaxValue);
        var maxIdentity = new PlatformIdentity(IdentityPlatform.Steam, ulong.MaxValue);
        var maxKey = new TerrainBlockKey(new PlanetId(ulong.MaxValue), 255, int.MinValue, int.MinValue, int.MinValue);
        var key64 = Text(ProtocolConstants.MaxRegistryKeyBytes);
        var planet = new PlanetParams(
            new PlanetId(ulong.MaxValue), new SystemId(ulong.MaxValue), ulong.MaxValue, key64, float.MaxValue, float.MaxValue, float.MinValue, float.MaxValue, float.MaxValue, float.MaxValue);
        var bigHeader = new ConstructHeaderRecord(
            maxEntity, maxFrame, uint.MaxValue, ConstructMode.Ship, new Vector3(float.MaxValue, float.MinValue, float.MaxValue), Quaternion.Identity, uint.MaxValue);
        var join = new PlayerJoinOp(
            new PlayerId(ulong.MaxValue),
            maxIdentity,
            Text(ProtocolConstants.MaxDisplayNameBytes),
            maxEntity,
            maxFrame,
            uint.MaxValue,
            new Vector3(float.MaxValue, float.MinValue, float.MaxValue),
            Quaternion.Identity);

        return
        [
            new ClientHello(
                ushort.MaxValue,
                ulong.MaxValue,
                maxIdentity,
                Text(ProtocolConstants.MaxDisplayNameBytes),
                Text(ProtocolConstants.MaxPasswordBytes),
                new byte[ProtocolConstants.MaxAuthTicketBytes]),
            new ServerHello(
                new WorldId(Guid.NewGuid()),
                new Tick(ulong.MaxValue),
                byte.MaxValue,
                new PlayerId(ulong.MaxValue),
                maxEntity,
                Enumerable.Repeat(planet, ProtocolConstants.MaxPlanetsInHello).ToArray(),
                Enumerable.Range(1, ProtocolConstants.MaxBlockTypes).Select(i => new RegistryEntry((ushort)i, key64)).ToArray(),
                Enumerable.Range(1, ProtocolConstants.MaxMaterials).Select(i => new RegistryEntry((ushort)i, key64)).ToArray()),
            new HandshakeReject(HandshakeRejectCode.IdentityMismatch, Text(ProtocolConstants.MaxRejectDetailBytes)),
            new ClientReady(),
            new TerrainCacheReport(
                new PlanetId(ulong.MaxValue),
                Enumerable.Range(0, ProtocolConstants.MaxTerrainCacheEntries).Select(_ => new TerrainBlockVersion(maxKey, uint.MaxValue)).ToArray()),
            new TerrainBlockData(maxKey, uint.MaxValue, new byte[ProtocolConstants.MaxTerrainBlockDataBytes]),
            new TerrainChecksums(
                new PlanetId(ulong.MaxValue),
                Enumerable.Range(0, ProtocolConstants.MaxTerrainChecksumEntries).Select(_ => new TerrainBlockChecksum(maxKey, ulong.MaxValue)).ToArray()),
            new ConstructState(
                bigHeader,
                ushort.MaxValue - 1,
                ushort.MaxValue,
                Enumerable.Range(0, ProtocolConstants.MaxChunksPerConstructStatePart).Select(WorstChunk).ToArray()),
            new ConstructRemoved(maxEntity),
            new OperationApplied(
                new OpId(ulong.MaxValue),
                new Tick(ulong.MaxValue),
                join,
                Enumerable.Range(0, ProtocolConstants.MaxConstructChangesPerOperation)
                    .Select(i => new ConstructChange(new EntityId(ulong.MaxValue - (ulong)(ProtocolConstants.MaxConstructChangesPerOperation - 1 - i)), uint.MaxValue, true))
                    .ToArray()),
            new IntentSubmit(
                uint.MaxValue,
                new PlaceBlockIntent(
                    new TerrainTarget(new PlanetId(ulong.MaxValue), new Vector3(float.MaxValue, 0, 0), new Vector3(0, float.MinValue, 0), new Vector3(0, 0, float.MaxValue)),
                    ushort.MaxValue,
                    23)),
            new IntentReply(uint.MaxValue, RejectReason.WrongPlanet, new OpId(ulong.MaxValue)),
            new InputBatch(
                uint.MaxValue,
                Enumerable.Range(0, ProtocolConstants.MaxInputsPerBatch)
                    .Select(_ => new PlayerInput(uint.MaxValue, -1f, 1f, float.MaxValue, float.MinValue, (InputButtons)ProtocolConstants.InputButtonsMask))
                    .ToArray()),
            Snapshot(),
            new SnapshotAck(uint.MaxValue),
            new EntitySpawn(
                maxEntity,
                EntityKind.Construct,
                ushort.MaxValue,
                new PlayerId(ulong.MaxValue),
                new SnapshotFrame(maxFrame, uint.MaxValue),
                FullState(ulong.MaxValue, 0),
                new byte[ProtocolConstants.MaxEntityKindDataBytes]),
            new EntityDespawn(maxEntity),
            new Ping(ulong.MaxValue),
            new Pong(ulong.MaxValue, new Tick(ulong.MaxValue)),
            new Disconnect(DisconnectCode.HandshakeTimeout),
            new ResyncRequest(new EntityId(ulong.MaxValue)),
            new TerrainSync(uint.MaxValue, 2, new PlanetId(ulong.MaxValue), new OpId(ulong.MaxValue), ushort.MaxValue, false,
                Enumerable.Range(0, 1024).Select(i => new TerrainSyncEntry(
                    new TerrainBlockKey(new PlanetId(ulong.MaxValue), 0, int.MinValue + i, int.MinValue, int.MinValue), uint.MaxValue, ulong.MaxValue)).ToArray()),
            new TerrainView(new PlanetId(ulong.MaxValue), uint.MaxValue, new Vector3(float.MaxValue), 16384, new Vector3(float.MinValue), 16384),
            new ServerStatus(true, 2, Text(ProtocolConstants.MaxRejectDetailBytes)),
            new JoinUnavailable(1, Text(ProtocolConstants.MaxRejectDetailBytes)),
            new StarSystemState(MaxStarSystem()),
            new ShipStatus(new EntityId(ulong.MaxValue), new Tick(ulong.MaxValue), float.MaxValue, float.MaxValue, float.MaxValue,
                (Horyzont.Core.Ships.Flight.ShipFlightFlags)0xFF, Horyzont.Core.Ships.ShipLaunchBlock.FuelReserve),
            new MarkersState(MaxMarkers()),
            new ContainerState(
                new ContainerId(ulong.MaxValue), RejectReason.None, ContainerKind.DroppedItem, ContainerAccess.Private, ProtocolConstants.MaxContainerSlots,
                uint.MaxValue, Enumerable.Range(0, ProtocolConstants.MaxContainerSlots).Select(MaxSlot).ToArray()),
            new ContainerDelta(
                new ContainerId(ulong.MaxValue), uint.MaxValue, Enumerable.Range(0, ProtocolConstants.MaxContainerSlots).Select(MaxSlot).ToArray()),
            new ContainerSubscribe(new ContainerId(ulong.MaxValue)),
            new ContainerUnsubscribe(new ContainerId(ulong.MaxValue)),
            MaxBlockEntityStates(),
            MaxPlayerProgressState(),
            new Horyzont.Core.Protocol.Messages.WorldSettingsState(Horyzont.Core.World.WorldSettings.Default),
            TestMessages.Vitals(
                ProtocolConstants.MaxVitalValue, ProtocolConstants.MaxVitalValue, ProtocolConstants.MaxVitalValue, ProtocolConstants.MaxVitalValue, true, true, true,
                ProtocolConstants.MaxRestWarmupTicks, ProtocolConstants.MaxRestRemainingTicks, 10) with { SampleTick = new Tick(ulong.MaxValue), ParentSeq = uint.MaxValue },
            MaxPropStates(),
            // 4096 entries of 64-byte keys would need 268 KiB; the registry fits the reliable channel with keys of up to 60 bytes
            // (K6-29W stage B deviation), and the writer refuses a larger frame.
            new ItemRegistry(Enumerable.Range(1, ProtocolConstants.MaxItemTypes).Select(i => new RegistryEntry((ushort)i, Text(60))).ToArray()),
        ];
    }
    /// <summary>
    /// The worst case of message 46 (K6-30, K6-32 review B1, 4 and 6; K6-34 B1 review P2 20): the construct id <c>ulong.MaxValue</c>, 512 stations with time (the most the
    /// protocol allows) with the longest recipe key and the largest ids and ticks, and 3584 running oxygen generators with the largest ids and ticks (28 bytes each, more than
    /// the 17 of a chest); 6-byte cells (2-byte coordinates). 2 + 10 + 2 + 512 x 103 + 3584 x 28 = 153 102 bytes of the 256 KiB channel.
    /// </summary>
    public static BlockEntityStates MaxBlockEntityStates() =>
        BlockEntityStatesOf(
            ProtocolConstants.MaxTimedStationsPerConstruct, 0, ProtocolConstants.MaxBlockEntitiesPerConstruct - ProtocolConstants.MaxTimedStationsPerConstruct);

    /// <summary>A state list of one construct with the largest values: the stations first (in cell order of the whole list), then the chests, then the generators.</summary>
    /// <param name="stations">Number of stations with time.</param>
    /// <param name="chests">Number of chests.</param>
    /// <param name="generators">Number of running oxygen generators.</param>
    /// <returns>The message.</returns>
    public static BlockEntityStates BlockEntityStatesOf(int stations, int chests, int generators = 0)
    {
        var construct = new EntityId(ulong.MaxValue);
        var cells = Enumerable.Range(0, stations + chests + generators)
            .Select(i => new CellPos(-2048 + (i % 64), -2048 + ((i / 64) % 64), -2048 + (i / 4096)))
            .OrderBy(cell => new BlockRef(construct, cell))
            .ToArray();
        var recipe = new string('r', ProtocolConstants.MaxRecipeKeyBytes);
        var entries = new List<BlockEntityEntry>(cells.Length);
        for (var i = 0; i < cells.Length; i++)
        {
            BlockEntityState state = i < stations
                ? new StationBlockState(new ContainerId(ulong.MaxValue - 1), new ContainerId(ulong.MaxValue), recipe, new Tick(ulong.MaxValue))
                : i < stations + chests
                    ? new ChestBlockState(new ContainerId(ulong.MaxValue))
                    : new OxygenGeneratorBlockState(new ContainerId(ulong.MaxValue), new Tick(ulong.MaxValue));
            entries.Add(new BlockEntityEntry(cells[i], state));
        }

        return new BlockEntityStates(construct, entries);
    }

    /// <summary>
    /// The largest message 48 (K6-32, K6-33): all 4096 item types (bits 1 to 4096), 256 keys of 64 UTF-8 bytes, ascending, and three food slots with the
    /// largest type (two bytes) and the largest tick (ten bytes), 36 bytes in all.
    /// </summary>
    public static PlayerProgressState MaxPlayerProgressState()
    {
        var bytes = new byte[ProtocolConstants.MaxDiscoveredBytes];
        Array.Fill(bytes, (byte)0xFF);
        bytes[0] = 0xFE;
        bytes[^1] = 0x01;
        var keys = Enumerable.Range(0, ProtocolConstants.MaxProgressKeys).Select(i => $"{i:D3}" + Text(ProtocolConstants.MaxRegistryKeyBytes - 3)).ToArray();
        var food = new FoodSlot(new ItemTypeId(ProtocolConstants.MaxItemTypes), new Tick(ulong.MaxValue));
        return new PlayerProgressState(ItemTypeBitSet.FromBytes(bytes), keys, new FoodSlots(food, food, food), MaxRespawnState());
    }

    /// <summary>
    /// The largest tail of message 48 (K6-35): a bed with the largest ids, the respawn transition, the largest revision and a full page of 64 tombstones with the largest ids and
    /// counters, and a death notice with the largest values; 64 x 40 bytes against 256 KiB.
    /// </summary>
    public static Horyzont.Core.Protocol.Messages.PlayerRespawnState MaxRespawnState() => new(
        new Horyzont.Core.Progress.BedRef(new EntityId(ulong.MaxValue), new CellPos(-2048, -2048, -2048), ushort.MaxValue),
        Horyzont.Core.Protocol.Messages.PlayerTransitionKind.Respawn,
        uint.MaxValue,
        ProtocolConstants.MaxTombstonePages - 1,
        ProtocolConstants.MaxTombstonePages,
        [.. Enumerable.Range(0, ProtocolConstants.MaxTombstonesPerPage).Select(i => new Horyzont.Core.Protocol.Messages.TombstoneEntry(
            new EntityId(ulong.MaxValue - (ulong)i), new ContainerId(ulong.MaxValue - (ulong)i), FrameRef.Of(new PlanetId(ulong.MaxValue)), uint.MaxValue, new Vector3(-1e6f, 1e6f, -1e6f)))],
        new Horyzont.Core.Protocol.Messages.DeathNotice(new OpId(ulong.MaxValue), uint.MaxValue));

    /// <summary>
    /// 4096 harvested props (K6-31): 64 regions of the last face with the largest region numbers, 64 entries each, ids of nine bytes and the
    /// largest ticks; about 19 bytes an entry, about 78 KiB of the 256 KiB channel.
    /// </summary>
    public static PropStates MaxPropStates()
    {
        const int face = 5;
        var regions = new List<PropRegionState>(ProtocolConstants.MaxPropRegionsPerMessage);
        for (var i = 0; i < ProtocolConstants.MaxPropRegionsPerMessage; i++)
        {
            var regionU = (1 << 17) - ProtocolConstants.MaxPropRegionsPerMessage + i;
            var entries = new List<HarvestedPropEntry>();
            for (var cell = 0; entries.Count < ProtocolConstants.MaxPropEntriesPerMessage / ProtocolConstants.MaxPropRegionsPerMessage; cell++)
            {
                entries.Add(new HarvestedPropEntry(
                    PropId.Create(face, (regionU * 8) + (cell >> 3), ((1 << 17) - 1) * 8 + (cell & 7), 3), new Tick(ulong.MaxValue)));
            }

            regions.Add(new PropRegionState(entries[0].Prop.Region, entries));
        }

        return new PropStates(new PlanetId(ulong.MaxValue), regions);
    }

    /// <summary>512 markers with the largest ids, owners and 32-byte names (about 37 KB, K5-27 decision 6).</summary>
    public static IReadOnlyList<MarkerRecord> MaxMarkers()
    {
        var markers = new List<MarkerRecord>();
        for (var i = 0; i < ProtocolConstants.MaxMarkersInState; i++)
        {
            markers.Add(new MarkerRecord(
                new MarkerId(MarkerIdAllocator.MaxValue - (ulong)i), i % 2 == 0 ? null : new PlayerId(ulong.MaxValue), new PlanetId(ulong.MaxValue),
                new Vector3(float.MaxValue, float.MinValue, float.MaxValue), Text(MarkerRules.MaxNameBytes), MarkerIcon.Poi, MarkerColor.Orange));
        }

        return markers;
    }

    /// <summary>The largest star system: eight bodies with the largest ids and coordinates (about 560 B, README Y5).</summary>
    public static StarSystemParams MaxStarSystem()
    {
        var home = new PlanetId(ulong.MaxValue);
        var bodies = new List<BodyPlacement> { new(home, 0, PoseD.Identity) };
        for (byte k = 1; k < StarSystemRules.MaxBodies; k++)
        {
            bodies.Add(new BodyPlacement(
                new PlanetId(ulong.MaxValue - k), k, new PoseD(new Vector3D(double.MaxValue, double.MinValue, double.MaxValue), QuaternionD.Identity)));
        }

        return new StarSystemParams(new SystemId(ulong.MaxValue), byte.MaxValue, home, float.MaxValue, bodies);
    }
}
