using System.Numerics;
using System.Text;

using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Markers;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Testing.Generators;

/// <summary>
/// FsCheck generators for every operation, intent, message and checkpoint record. They produce only values that are valid according to K1-03
/// and within the <see cref="ProtocolConstants"/> limits: finite floats, normalized quaternions, in-bounds cells, well-formed blocks,
/// absent <see cref="EntityState"/> fields at their default value (rotation = identity) and frame indexes that point into the snapshot frame table.
/// </summary>
public static class CodecGen
{
    private const int ChunkCells = 4096;

    private static readonly string[] _textPieces =
    [
        "a", "b", "Z", "7", " ", "_", ":", "/", "core", "ą", "ż", "ó", "€", "日", "本", "\U0001D11E", "\U0001F600",
    ];

    private static Gen<ulong> Full64 { get; } =
        Gen.Choose(int.MinValue, int.MaxValue).SelectMany(hi => Gen.Choose(int.MinValue, int.MaxValue).Select(lo => ((ulong)(uint)hi << 32) | (uint)lo));

    private static Gen<double> Gaussian { get; } =
        Gen.Choose(1, 1_000_000).SelectMany(a => Gen.Choose(1, 1_000_000).Select(b =>
            Math.Sqrt(-2.0 * Math.Log(a / 1_000_001.0)) * Math.Cos(2.0 * Math.PI * (b / 1_000_001.0))));

    /// <summary>World ids.</summary>
    public static Gen<WorldId> World { get; } =
        Gen.Choose(int.MinValue, int.MaxValue).ArrayOf(4).Select(a =>
        {
            var bytes = new byte[16];
            for (var i = 0; i < 4; i++)
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), a[i]);
            }

            return new WorldId(new Guid(bytes));
        });

    /// <summary>Unsigned 64-bit values with a bias towards small numbers and the encoding length boundaries.</summary>
    public static Gen<ulong> VarU64 { get; } = Gen.Frequency(
        (4, Gen.Choose(0, 300).Select(x => (ulong)x)),
        (3, Gen.Choose(0, 70_000).Select(x => (ulong)x)),
        (2, Gen.Choose(0, int.MaxValue).Select(x => (ulong)x)),
        (2, Full64),
        (1, Gen.Elements(0UL, 127UL, 128UL, 16383UL, 16384UL, uint.MaxValue, (ulong)uint.MaxValue + 1, ulong.MaxValue)));

    /// <summary>Unsigned 32-bit values with a bias towards small numbers and the encoding length boundaries.</summary>
    public static Gen<uint> VarU32 { get; } = Gen.Frequency(
        (4, Gen.Choose(0, 300).Select(x => (uint)x)),
        (3, Gen.Choose(0, 70_000).Select(x => (uint)x)),
        (2, Gen.Choose(0, int.MaxValue).Select(x => (uint)x)),
        (2, Gen.Choose(int.MinValue, int.MaxValue).Select(x => (uint)x)),
        (1, Gen.Elements(0U, 127U, 128U, 16383U, 16384U, uint.MaxValue)));

    /// <summary>Values that fit in <see cref="ushort"/>, biased like <see cref="VarU32"/>.</summary>
    public static Gen<ushort> VarU16 { get; } = Gen.Frequency(
        (4, Gen.Choose(0, 300).Select(x => (ushort)x)),
        (3, Gen.Choose(0, ushort.MaxValue).Select(x => (ushort)x)),
        (1, Gen.Elements((ushort)0, (ushort)127, (ushort)128, (ushort)16383, (ushort)16384, ushort.MaxValue)));

    /// <summary>Signed 32-bit values for varint fields.</summary>
    public static Gen<int> VarI32 { get; } = Gen.Frequency(
        (4, Gen.Choose(-300, 300)),
        (3, Gen.Choose(-70_000, 70_000)),
        (2, Gen.Choose(int.MinValue, int.MaxValue)),
        (1, Gen.Elements(0, -1, 1, 63, 64, -64, -65, int.MinValue, int.MaxValue)));

    /// <summary>Finite floats of mixed magnitude, including zero, subnormals and very large values.</summary>
    public static Gen<float> Finite { get; } = Gen.Frequency(
        (5, Gen.Choose(-1_000_000, 1_000_000).Select(x => x / 1000f)),
        (2, Gen.Choose(-100, 100).Select(x => x / 10f)),
        (1, Gen.Elements(0f, -0f, 1f, -1f, float.Epsilon, float.MaxValue, float.MinValue)),
        (1, Gen.Choose(int.MinValue, int.MaxValue).Select(BitConverter.Int32BitsToSingle).Where(float.IsFinite)));

    /// <summary>Positive finite floats.</summary>
    public static Gen<float> PositiveFinite { get; } = Gen.Frequency(
        (4, Gen.Choose(1, 1_000_000).Select(x => x / 100f)),
        (1, Gen.Choose(1, int.MaxValue).Select(BitConverter.Int32BitsToSingle).Where(x => float.IsFinite(x) && x > 0)));

    /// <summary>Floats in [-1, 1].</summary>
    public static Gen<float> UnitRange { get; } = Gen.Frequency(
        (4, Gen.Choose(-1000, 1000).Select(x => x / 1000f)),
        (1, Gen.Elements(-1f, 0f, 1f, 0.5f, -0.5f)));

    /// <summary>Floats that survive a round trip through <see cref="Half"/> without overflow (|v| at most 60000).</summary>
    public static Gen<float> HalfRange { get; } = Gen.Frequency(
        (5, Gen.Choose(-20_000, 20_000).Select(x => x / 100f)),
        (1, Gen.Choose(-60_000, 60_000).Select(x => (float)x)),
        (1, Gen.Elements(0f, 1f, -1f, 65504f, -65504f)));

    /// <summary>Vectors with finite components.</summary>
    public static Gen<Vector3> Vector { get; } = Gen3(Finite, Finite, Finite, (x, y, z) => new Vector3(x, y, z));

    /// <summary>Vectors whose components fit in a half.</summary>
    public static Gen<Vector3> HalfVector { get; } = Gen3(HalfRange, HalfRange, HalfRange, (x, y, z) => new Vector3(x, y, z));

    /// <summary>Normalized quaternions: four N(0,1) components, then normalization.</summary>
    public static Gen<Quaternion> UnitQuaternion { get; } = Gen.Frequency(
        (12, Gen4(Gaussian, Gaussian, Gaussian, Gaussian, (x, y, z, w) => Normalize(new Quaternion((float)x, (float)y, (float)z, (float)w)))),
        (1, Gen.Elements(
            Quaternion.Identity,
            new Quaternion(1, 0, 0, 0),
            new Quaternion(0, -1, 0, 0),
            new Quaternion(0, 0, 0, -1),
            Normalize(new Quaternion(0.5f, 0.5f, 0.5f, 0.5f)))));

    /// <summary>Entity ids from 1 (persistent and transient).</summary>
    public static Gen<EntityId> EntityId { get; } = VarU64.Select(v => new EntityId(Math.Max(1, v)));

    /// <summary>Player ids from 1.</summary>
    public static Gen<PlayerId> PlayerId { get; } = VarU64.Select(v => new PlayerId(Math.Max(1, v)));

    /// <summary>Planet ids from 1.</summary>
    public static Gen<PlanetId> PlanetId { get; } = VarU64.Select(v => new PlanetId(Math.Max(1, v)));

    /// <summary>System ids from 1.</summary>
    public static Gen<SystemId> SystemId { get; } = VarU64.Select(v => new SystemId(Math.Max(1, v)));

    /// <summary>Operation ids.</summary>
    public static Gen<OpId> OpId { get; } = VarU64.Select(v => new OpId(v));

    /// <summary>Ticks.</summary>
    public static Gen<Tick> Tick { get; } = VarU64.Select(v => new Tick(v));

    /// <summary>Frame references of every kind.</summary>
    public static Gen<FrameRef> FrameRef { get; } = Gen.Elements(FrameKind.System, FrameKind.Planet, FrameKind.Construct)
        .SelectMany(k => VarU64.Select(id => new FrameRef(k, id)));

    /// <summary>Platform identities (the value is a full 64-bit number, like a SteamID64).</summary>
    public static Gen<PlatformIdentity> Identity { get; } = Gen.Elements(IdentityPlatform.Dev, IdentityPlatform.Steam)
        .SelectMany(p => Full64.Select(v => new PlatformIdentity(p, v)));

    /// <summary>Cells inside the construct bounds.</summary>
    public static Gen<CellPos> Cell { get; } = Gen3(
        Gen.Choose(-CellPos.Limit, CellPos.Limit - 1),
        Gen.Choose(-CellPos.Limit, CellPos.Limit - 1),
        Gen.Choose(-CellPos.Limit, CellPos.Limit - 1),
        (x, y, z) => new CellPos(x, y, z));

    /// <summary>Chunk positions inside the construct bounds.</summary>
    public static Gen<ChunkPos> Chunk { get; } = Gen3(Gen.Choose(-128, 127), Gen.Choose(-128, 127), Gen.Choose(-128, 127), (x, y, z) => new ChunkPos(x, y, z));

    /// <summary>Non-empty, well-formed block states.</summary>
    public static Gen<BlockState> Block { get; } = Gen4(
        Gen.Frequency((4, Gen.Choose(1, 300)), (2, Gen.Choose(1, ushort.MaxValue))),
        Gen.Choose(0, BlockState.OrientationCount - 1),
        Gen.Choose(0, BlockState.CostRevisionCount - 1),
        Gen.Choose(0, BlockState.FunctionStateCount - 1),
        (t, o, c, f) => BlockState.Create((ushort)t, (byte)o, (byte)c, (byte)f));

    /// <summary>Block states including the empty cell.</summary>
    public static Gen<BlockState> BlockOrEmpty { get; } = Gen.Frequency((1, Gen.Constant(BlockState.Empty)), (3, Block));

    /// <summary>Terrain block keys.</summary>
    public static Gen<TerrainBlockKey> TerrainKey { get; } =
        from planet in PlanetId
        from lod in Gen.Choose(0, 255)
        from x in VarI32
        from y in VarI32
        from z in VarI32
        select new TerrainBlockKey(planet, (byte)lod, x, y, z);

    /// <summary>Terrain block versions.</summary>
    public static Gen<TerrainBlockVersion> TerrainVersion { get; } = TerrainKey.SelectMany(k => VarU32.Select(v => new TerrainBlockVersion(k, v)));

    /// <summary>Terrain block checksums.</summary>
    public static Gen<TerrainBlockChecksum> TerrainChecksum { get; } = TerrainKey.SelectMany(k => Full64.Select(h => new TerrainBlockChecksum(k, h)));

    /// <summary>Snapshot frames.</summary>
    public static Gen<SnapshotFrame> SnapshotFrame { get; } = FrameRef.SelectMany(f => VarU32.Select(s => new SnapshotFrame(f, s)));

    /// <summary>Chunks of 4096 well-formed block states in assorted shapes (empty, uniform, sparse, banded, worst-case alternating, random).</summary>
    public static Gen<uint[]> ChunkBlocks { get; } = Gen.Choose(0, 5).SelectMany(shape => Gen.Choose(0, int.MaxValue).SelectMany(seed => BuildChunk(shape, seed)));

    /// <summary>Operations of every kind (1-7, 20-23 and 30-35).</summary>
    public static Gen<Operation> Operation => Gen.OneOf(
        TerrainEditOp.Select(o => (Operation)o),
        ConstructCreateOp.Select(o => (Operation)o),
        BlockSetOp.Select(o => (Operation)o),
        BlockClearOp.Select(o => (Operation)o),
        PlayerJoinOp.Select(o => (Operation)o),
        PlayerLeaveOp.Select(o => (Operation)o),
        EntityReparentOp.Select(o => (Operation)o),
        ShipModeSetOp.Select(o => (Operation)o),
        MarkerSetOp.Select(o => (Operation)o),
        MarkerRemoveOp.Select(o => (Operation)o),
        ShipFuelAddOp.Select(o => (Operation)o),
        CompoundOp.Select(o => (Operation)o),
        ItemTransferOp.Select(o => (Operation)o),
        ItemSwapOp.Select(o => (Operation)o),
        ItemCreateOp.Select(o => (Operation)o),
        ItemDestroyOp.Select(o => (Operation)o),
        ContainerCreateOp.Select(o => (Operation)o));

    /// <summary>Terrain edit operations.</summary>
    public static Gen<TerrainEditOp> TerrainEditOp { get; } =
        from planet in PlanetId
        from center in Vector
        from radius in PositiveFinite
        from mode in Gen.Elements(TerrainEditMode.Remove, TerrainEditMode.Add)
        from material in Gen.Choose(0, 255)
        select new TerrainEditOp(planet, TerrainShape.Sphere, center, radius, mode, (byte)material);

    /// <summary>Construct creation operations.</summary>
    public static Gen<ConstructCreateOp> ConstructCreateOp { get; } =
        from id in EntityId
        from parent in FrameRef
        from origin in Vector
        from rotation in UnitQuaternion
        from cell in Cell
        from block in Block
        select new ConstructCreateOp(id, parent, origin, rotation, cell, block);

    /// <summary>Block placement operations.</summary>
    public static Gen<BlockSetOp> BlockSetOp { get; } =
        from id in EntityId
        from cell in Cell
        from block in Block
        select new BlockSetOp(id, cell, block);

    /// <summary>Block removal operations.</summary>
    public static Gen<BlockClearOp> BlockClearOp { get; } =
        from id in EntityId
        from cell in Cell
        select new BlockClearOp(id, cell);

    /// <summary>Player join operations.</summary>
    public static Gen<PlayerJoinOp> PlayerJoinOp { get; } =
        from player in PlayerId
        from identity in Identity
        from name in Text(0, ProtocolConstants.MaxDisplayNameBytes)
        from entity in EntityId
        from frame in FrameRef
        from parentSeq in VarU32
        from position in Vector
        from rotation in UnitQuaternion
        select new PlayerJoinOp(player, identity, name, entity, frame, parentSeq, position, rotation);

    /// <summary>Player leave operations.</summary>
    public static Gen<PlayerLeaveOp> PlayerLeaveOp { get; } =
        from player in PlayerId
        from frame in FrameRef
        from parentSeq in VarU32
        from position in Vector
        from rotation in UnitQuaternion
        from pitch in Finite
        select new PlayerLeaveOp(player, frame, parentSeq, position, rotation, pitch);

    /// <summary>Entity reparent operations.</summary>
    public static Gen<EntityReparentOp> EntityReparentOp { get; } =
        from entity in EntityId
        from parent in FrameRef
        from parentSeq in VarU32
        from position in Vector
        from rotation in UnitQuaternion
        from velocity in Vector
        from seat in Optional(Cell)
        select new EntityReparentOp(entity, parent, parentSeq, position, rotation, velocity, seat);

    /// <summary>Item type ids, mostly inside the registry range, sometimes any 16-bit value (operations carry them unchecked).</summary>
    public static Gen<ItemTypeId> ItemType { get; } =
        Gen.Frequency((3, Gen.Choose(0, ProtocolConstants.MaxItemTypes)), (1, Gen.Choose(0, ushort.MaxValue))).Select(v => new ItemTypeId((ushort)v));

    /// <summary>Container ids from 1.</summary>
    public static Gen<ContainerId> ContainerId { get; } = VarU64.Select(v => new ContainerId(Math.Max(1, v)));

    /// <summary>Slot references.</summary>
    public static Gen<SlotRef> SlotRef { get; } =
        from container in ContainerId
        from slot in VarU16
        select new SlotRef(container, slot);

    /// <summary>Container hosts of every kind.</summary>
    public static Gen<ContainerHost> ContainerHost { get; } = Gen.OneOf(
        PlayerId.Select(p => Horyzont.Core.Items.ContainerHost.Player(p)),
        EntityId.SelectMany(e => Cell.Select(c => Horyzont.Core.Items.ContainerHost.ConstructBlock(e, c))),
        EntityId.Select(e => Horyzont.Core.Items.ContainerHost.PersistentEntity(e)),
        PlanetId.SelectMany(p => VarU64.SelectMany(s => VarU64.Select(n => Horyzont.Core.Items.ContainerHost.Poi(p, s, n)))));

    /// <summary>Non-empty item stacks within the codec limits.</summary>
    public static Gen<ItemStack> ItemStack { get; } =
        from type in Gen.Choose(1, ProtocolConstants.MaxItemTypes)
        from count in Gen.Frequency((3, Gen.Choose(1, 500)), (1, Gen.Choose(1, ushort.MaxValue)))
        select new ItemStack(new ItemTypeId((ushort)type), (ushort)count);

    /// <summary>Item transfer operations.</summary>
    public static Gen<ItemTransferOp> ItemTransferOp { get; } =
        from source in SlotRef
        from target in SlotRef
        from type in ItemType
        from count in VarU16
        from limit in VarU16
        select new ItemTransferOp(source, target, type, count, limit);

    /// <summary>Item swap operations.</summary>
    public static Gen<ItemSwapOp> ItemSwapOp { get; } =
        from a in SlotRef
        from b in SlotRef
        from expectedA in ItemType
        from expectedB in ItemType
        select new ItemSwapOp(a, b, expectedA, expectedB);

    /// <summary>Item create operations of every source.</summary>
    public static Gen<ItemCreateOp> ItemCreateOp { get; } =
        from slot in SlotRef
        from type in ItemType
        from count in VarU16
        from limit in VarU16
        from source in Gen.Elements(Enum.GetValues<ItemSource>())
        select new ItemCreateOp(slot, type, count, limit, source);

    /// <summary>Item destroy operations of every sink.</summary>
    public static Gen<ItemDestroyOp> ItemDestroyOp { get; } =
        from slot in SlotRef
        from type in ItemType
        from count in VarU16
        from sink in Gen.Elements(Enum.GetValues<ItemSink>())
        select new ItemDestroyOp(slot, type, count, sink);

    /// <summary>Container create operations of every kind, host kind and access.</summary>
    public static Gen<ContainerCreateOp> ContainerCreateOp { get; } =
        from id in ContainerId
        from kind in Gen.Elements(Enum.GetValues<ContainerKind>())
        from host in ContainerHost
        from owner in VarU64
        from access in Gen.Elements(Enum.GetValues<ContainerAccess>())
        from size in VarU16
        select new ContainerCreateOp(id, kind, host, new PlayerId(owner), access, size);

    /// <summary>Item components of compound operations.</summary>
    public static Gen<Operation> ItemComponent { get; } = Gen.OneOf(
        ItemTransferOp.Select(o => (Operation)o),
        ItemSwapOp.Select(o => (Operation)o),
        ItemCreateOp.Select(o => (Operation)o),
        ItemDestroyOp.Select(o => (Operation)o),
        ContainerCreateOp.Select(o => (Operation)o));

    /// <summary>Compound operations of 1..6 item components, about half of them ending with a terrain edit.</summary>
    public static Gen<CompoundOp> CompoundOp { get; } =
        from count in Gen.Choose(1, 6)
        from items in ItemComponent.ArrayOf(count)
        from edit in Gen.Frequency((1, Gen.Constant<TerrainEditOp?>(null)), (1, TerrainEditOp.Select(e => (TerrainEditOp?)e)))
        select new CompoundOp(edit is null ? items : [.. items, edit]);

    /// <summary>Container records: 1..256 slots, some of them filled.</summary>
    public static Gen<ContainerRecord> ContainerRecord { get; } =
        from id in ContainerId
        from kind in Gen.Elements(Enum.GetValues<ContainerKind>())
        from host in ContainerHost
        from owner in VarU64
        from access in Gen.Elements(Enum.GetValues<ContainerAccess>())
        from size in Gen.Frequency((4, Gen.Choose(1, 40)), (1, Gen.Choose(1, ProtocolConstants.MaxContainerSlots)))
        from version in VarU32
        from slots in Gen.Frequency((1, Gen.Constant(Horyzont.Core.Items.ItemStack.Empty)), (1, ItemStack)).ArrayOf(size)
        select new ContainerRecord(id, kind, host, new PlayerId(owner), access, (ushort)size, version, slots);

    /// <summary>Intents of all five kinds.</summary>
    public static Gen<Intent> Intent => Gen.OneOf(
        TerrainToolIntent.Select(i => (Intent)i),
        PlaceBlockIntent.Select(i => (Intent)i),
        RemoveBlockIntent.Select(i => (Intent)i),
        ShipModeIntent.Select(i => (Intent)i),
        MarkerSetIntent.Select(i => (Intent)i),
        MarkerRemoveIntent.Select(i => (Intent)i),
        RefuelIntent.Select(i => (Intent)i));

    /// <summary>Terrain tool intents.</summary>
    public static Gen<TerrainToolIntent> TerrainToolIntent { get; } =
        from planet in PlanetId
        from origin in Vector
        from direction in Vector
        from radius in PositiveFinite
        from mode in Gen.Elements(TerrainEditMode.Remove, TerrainEditMode.Add)
        from material in Gen.Choose(0, 255)
        select new TerrainToolIntent(planet, origin, direction, radius, mode, (byte)material);

    /// <summary>Block placement targets (construct face or terrain).</summary>
    public static Gen<BlockTarget> BlockTarget { get; } = Gen.OneOf(
        (from id in EntityId
         from cell in Cell
         from face in Gen.Choose(0, 5)
         select (BlockTarget)new ConstructFaceTarget(id, cell, (Face)face)),
        (from planet in PlanetId
         from origin in Vector
         from direction in Vector
         from look in Vector
         select (BlockTarget)new TerrainTarget(planet, origin, direction, look)));

    /// <summary>Place block intents.</summary>
    public static Gen<PlaceBlockIntent> PlaceBlockIntent { get; } =
        from target in BlockTarget
        from type in Gen.Frequency((4, Gen.Choose(1, 300)), (1, Gen.Choose(1, ushort.MaxValue)))
        from orientation in Gen.Choose(0, BlockState.OrientationCount - 1)
        select new PlaceBlockIntent(target, (ushort)type, (byte)orientation);

    /// <summary>Remove block intents.</summary>
    public static Gen<RemoveBlockIntent> RemoveBlockIntent { get; } =
        from id in EntityId
        from cell in Cell
        select new RemoveBlockIntent(id, cell);

    /// <summary>Ship mode switch operations.</summary>
    public static Gen<ShipModeSetOp> ShipModeSetOp { get; } =
        from id in EntityId
        from mode in Gen.Elements(ConstructMode.Static, ConstructMode.Ship)
        from parent in FrameRef
        from parentSeq in VarU32
        from origin in Vector
        from rotation in UnitQuaternion
        select new ShipModeSetOp(id, mode, parent, parentSeq, origin, rotation);

    /// <summary>Markers (ids within the marker id range; the name is any 1..32 byte text).</summary>
    public static Gen<MarkerRecord> Marker { get; } =
        from id in Gen.Choose(1, int.MaxValue)
        from owner in Optional(Gen.Choose(1, int.MaxValue))
        from planet in PlanetId
        from position in Vector
        from name in Text(1, MarkerRules.MaxNameBytes)
        from icon in Gen.Choose(0, 7)
        from color in Gen.Choose(0, 7)
        select new MarkerRecord(
            new MarkerId((ulong)id), owner is { } o ? new PlayerId((ulong)o) : null, planet, position, name, (MarkerIcon)icon, (MarkerColor)color);

    /// <summary>Marker set operations.</summary>
    public static Gen<MarkerSetOp> MarkerSetOp { get; } =
        Marker.Select(m => new MarkerSetOp(m.Id, m.Owner, m.Planet, m.Position, m.Name, m.Icon, m.Color));

    /// <summary>Marker remove operations.</summary>
    public static Gen<MarkerRemoveOp> MarkerRemoveOp { get; } = Gen.Choose(1, int.MaxValue).Select(v => new MarkerRemoveOp(new MarkerId((ulong)v)));

    /// <summary>Marker set intents.</summary>
    public static Gen<MarkerSetIntent> MarkerSetIntent { get; } =
        from m in Marker
        from edit in Gen.Elements(false, true)
        select new MarkerSetIntent(edit ? m.Id : MarkerId.None, m.Owner is null, m.Planet, m.Position, m.Name, m.Icon, m.Color);

    /// <summary>Marker remove intents.</summary>
    public static Gen<MarkerRemoveIntent> MarkerRemoveIntent { get; } = Gen.Choose(1, int.MaxValue).Select(v => new MarkerRemoveIntent(new MarkerId((ulong)v)));

    /// <summary>Fuel add operations (K5-24): units finite and positive, capacity finite and non-negative.</summary>
    public static Gen<ShipFuelAddOp> ShipFuelAddOp { get; } =
        from id in EntityId
        from units in Gen.Elements(0.5f, 1f, 1000f, 2000f, float.Epsilon, float.MaxValue)
        from capacity in Gen.Elements(0f, 1000f, 2000f, float.MaxValue)
        select new ShipFuelAddOp(id, units, capacity);

    /// <summary>Refuel intents (K5-24).</summary>
    public static Gen<RefuelIntent> RefuelIntent { get; } =
        from ship in EntityId
        from station in EntityId
        from cell in Cell
        from origin in Vector
        from direction in Vector
        select new RefuelIntent(ship, station, cell, origin, direction);

    /// <summary>Ship mode intents.</summary>
    public static Gen<ShipModeIntent> ShipModeIntent { get; } =
        from id in EntityId
        from cell in Cell
        from mode in Gen.Elements(ConstructMode.Static, ConstructMode.Ship)
        from origin in Vector
        from direction in Vector
        select new ShipModeIntent(id, cell, mode, origin, direction);

    /// <summary>Construct header records.</summary>
    public static Gen<ConstructHeaderRecord> ConstructHeader { get; } =
        from id in EntityId
        from parent in FrameRef
        from parentSeq in VarU32
        from mode in Gen.Elements(ConstructMode.Static, ConstructMode.Ship)
        from origin in Vector
        from rotation in UnitQuaternion
        from version in VarU32
        select new ConstructHeaderRecord(id, parent, parentSeq, mode, origin, rotation, version);

    /// <summary>Player records.</summary>
    public static Gen<PlayerRecord> PlayerRecord { get; } =
        from player in PlayerId
        from identity in Identity
        from name in Text(0, ProtocolConstants.MaxDisplayNameBytes)
        from entity in EntityId
        from frame in FrameRef
        from parentSeq in VarU32
        from seat in Optional(Cell)
        from position in Vector
        from rotation in UnitQuaternion
        from pitch in Finite
        from online in Gen.Elements(true, false)
        select new PlayerRecord(player, identity, name, entity, frame, parentSeq, seat, position, rotation, pitch, online);

    /// <summary>Planet parameters.</summary>
    public static Gen<PlanetParams> Planet { get; } =
        from id in PlanetId
        from system in SystemId
        from seed in Full64
        from profile in Text(1, ProtocolConstants.MaxRegistryKeyBytes)
        from radius in Gen.Choose(1000, 100_000).Select(x => (float)x)
        from voxel in PositiveFinite
        from gravity in Finite
        from atmosphere in PositiveFinite
        from core in Gen.Choose(0, 900).Select(x => (float)x)
        from day in PositiveFinite
        select new PlanetParams(id, system, seed, profile, radius, voxel, gravity, atmosphere, core, day);

    /// <summary>Registry entries with ids in 1..<paramref name="maxId"/> and keys of 0 to 64 bytes.</summary>
    /// <param name="maxId">Largest id.</param>
    /// <returns>The generator.</returns>
    public static Gen<RegistryEntry> RegistryEntry(int maxId) =>
        from id in Gen.Frequency((3, Gen.Choose(1, maxId)), (1, Gen.Elements(1, maxId)))
        from key in Text(0, ProtocolConstants.MaxRegistryKeyBytes)
        select new RegistryEntry((ushort)id, key);

    /// <summary>Construct chunk records for the given owner; the chunk position varies.</summary>
    /// <param name="construct">Owning construct.</param>
    /// <returns>The generator.</returns>
    public static Gen<ConstructChunkRecord> ChunkRecord(EntityId construct) =>
        from pos in Chunk
        from blocks in ChunkBlocks
        select new ConstructChunkRecord(new ConstructChunkKey(construct, pos), blocks);

    /// <summary>Strings of well-formed UTF-8 with between <paramref name="minBytes"/> and <paramref name="maxBytes"/> encoded bytes.</summary>
    /// <param name="minBytes">Minimum encoded length (at most 1 is honoured exactly for non-empty requests).</param>
    /// <param name="maxBytes">Maximum encoded length.</param>
    /// <returns>The generator.</returns>
    public static Gen<string> Text(int minBytes, int maxBytes) =>
        Gen.Frequency(
            (3, Gen.Choose(minBytes, Math.Min(maxBytes, 8))),
            (2, Gen.Choose(minBytes, maxBytes)),
            (1, Gen.Constant(maxBytes)))
        .SelectMany(target => Gen.Elements(_textPieces).ArrayOf(Math.Max(1, target)).Select(pieces => FitText(pieces, minBytes, target)));

    /// <summary>Byte arrays with a length between 0 and <paramref name="maxBytes"/>, biased towards short ones.</summary>
    /// <param name="maxBytes">Maximum length.</param>
    /// <returns>The generator.</returns>
    public static Gen<byte[]> Bytes(int maxBytes) =>
        Gen.Frequency(
            (4, Gen.Choose(0, Math.Min(maxBytes, 16))),
            (2, Gen.Choose(0, Math.Min(maxBytes, 300))),
            (1, Gen.Choose(0, maxBytes)))
        .SelectMany(len => Gen.Choose(0, 255).ArrayOf(len).Select(a => Array.ConvertAll(a, x => (byte)x)));

    /// <summary>Wraps a generator in the "T?" shape used by optional record fields.</summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="inner">Generator of present values.</param>
    /// <returns>A generator of values that are present about three times out of four.</returns>
    public static Gen<T?> Optional<T>(Gen<T> inner)
        where T : struct =>
        Gen.Frequency((1, Gen.Constant<T?>(null)), (3, inner.Select(v => (T?)v)));

    /// <summary>Entity state with the given number of snapshot frames available; absent fields hold defaults.</summary>
    /// <param name="frameCount">Number of frames in the snapshot frame table (at least 1).</param>
    /// <returns>The generator.</returns>
    public static Gen<EntityState> State(int frameCount) =>
        from entity in EntityId
        from fields in Gen.Choose(0, 15)
        from frameIndex in Gen.Choose(0, frameCount - 1)
        from parentSeq in VarU32
        from position in Vector
        from rotation in UnitQuaternion
        from velocity in HalfVector
        from motion in Gen.Choose(0, 255)
        select MakeState(entity, (EntityStateFields)fields, (byte)frameIndex, parentSeq, position, rotation, velocity, (byte)motion);

    /// <summary>Messages of all assigned types.</summary>
    public static Gen<Message> Message => Gen.OneOf(
        ClientHello.Select(m => (Message)m),
        ServerHello.Select(m => (Message)m),
        HandshakeReject.Select(m => (Message)m),
        Gen.Constant<Message>(new ClientReady()),
        TerrainCacheReport.Select(m => (Message)m),
        TerrainBlockData.Select(m => (Message)m),
        TerrainChecksums.Select(m => (Message)m),
        ConstructState.Select(m => (Message)m),
        ConstructRemoved.Select(m => (Message)m),
        OperationApplied.Select(m => (Message)m),
        IntentSubmit.Select(m => (Message)m),
        IntentReply.Select(m => (Message)m),
        InputBatch.Select(m => (Message)m),
        Snapshot.Select(m => (Message)m),
        SnapshotAck.Select(m => (Message)m),
        EntitySpawn.Select(m => (Message)m),
        EntityDespawn.Select(m => (Message)m),
        Ping.Select(m => (Message)m),
        Pong.Select(m => (Message)m),
        Disconnect.Select(m => (Message)m),
        Full64.Select(v => (Message)new ResyncRequest(new EntityId(v == 0 ? 1 : v))),
        (from seq in VarU32
         from hash in Full64
         from x in Gen.Choose(-100, 100)
         select (Message)new TerrainSync(Math.Max(1u, seq), 0, new PlanetId(1), default, 0, false,
             [new TerrainSyncEntry(new TerrainBlockKey(new PlanetId(1), 0, x, 0, 0), seq, hash)])),
        (from seq in VarU32
         from position in Vector
         select (Message)new TerrainView(new PlanetId(1), Math.Max(1u, seq), position, 2048, position, 64)),
        ServerStatus.Select(m => (Message)m),
        JoinUnavailable.Select(m => (Message)m),
        Full64.Select(seed => (Message)new StarSystemState(Horyzont.Core.Space.StarSystemGenesis.CreateWorldBodies(seed, "core:planet/1").System)),
        ShipStatus.Select(m => (Message)m),
        (from first in Marker
         from second in Marker
         from count in Gen.Choose(0, 2)
         select (Message)new MarkersState(new[] { first, second }.Take(count).ToArray())));


    /// <summary>Ship status messages (K5-26): any ship and tick, non-negative finite values, any flags, a defined launch block.</summary>
    public static Gen<ShipStatus> ShipStatus { get; } =
        from ship in EntityId
        from tick in Tick
        from fuel in PositiveFinite
        from capacity in PositiveFinite
        from limit in PositiveFinite
        from flags in Gen.Choose(0, 255)
        from block in Gen.Elements(Enum.GetValues<Horyzont.Core.Ships.ShipLaunchBlock>())
        select new ShipStatus(ship, tick, fuel, capacity, limit, (Horyzont.Core.Ships.Flight.ShipFlightFlags)flags, block);

    /// <summary>Server status messages: any flag and reason, detail of 0 to the codec maximum in UTF-8 bytes.</summary>
    public static Gen<ServerStatus> ServerStatus { get; } =
        from readOnly in Gen.Elements(false, true)
        from reason in Gen.Choose(0, 255)
        from detail in Text(0, ProtocolConstants.MaxRejectDetailBytes)
        select new ServerStatus(readOnly, (byte)reason, detail);

    /// <summary>Join refusals: any reason, detail of 0 to the codec maximum in UTF-8 bytes.</summary>
    public static Gen<JoinUnavailable> JoinUnavailable { get; } =
        from reason in Gen.Choose(0, 255)
        from detail in Text(0, ProtocolConstants.MaxRejectDetailBytes)
        select new JoinUnavailable((byte)reason, detail);

    /// <summary>Client hello messages.</summary>
    public static Gen<ClientHello> ClientHello { get; } =
        from version in VarU16
        from build in Full64
        from identity in Identity
        from name in Text(0, ProtocolConstants.MaxDisplayNameBytes)
        from password in Text(0, ProtocolConstants.MaxPasswordBytes)
        from ticket in Bytes(ProtocolConstants.MaxAuthTicketBytes)
        select new ClientHello(version, build, identity, name, password, ticket);

    /// <summary>Server hello messages.</summary>
    public static Gen<ServerHello> ServerHello { get; } =
        from world in World
        from tick in Tick
        from rate in Gen.Choose(0, 255)
        from player in PlayerId
        from entity in EntityId
        from planets in ListUpTo(Planet, ProtocolConstants.MaxPlanetsInHello, 6)
        from blocks in ListUpTo(RegistryEntry(ProtocolConstants.MaxBlockTypes), ProtocolConstants.MaxBlockTypes, 12)
        from materials in ListUpTo(RegistryEntry(ProtocolConstants.MaxMaterials), ProtocolConstants.MaxMaterials, 8)
        select new ServerHello(world, tick, (byte)rate, player, entity, planets, blocks, materials);

    /// <summary>Handshake rejections with a known code.</summary>
    public static Gen<HandshakeReject> HandshakeReject { get; } =
        from code in Gen.Choose(1, 7)
        from detail in Text(0, ProtocolConstants.MaxRejectDetailBytes)
        select new HandshakeReject((HandshakeRejectCode)code, detail);

    /// <summary>Terrain cache reports.</summary>
    public static Gen<TerrainCacheReport> TerrainCacheReport { get; } =
        from planet in PlanetId
        from entries in ListUpTo(TerrainVersion, ProtocolConstants.MaxTerrainCacheEntries, 10)
        select new TerrainCacheReport(planet, entries);

    /// <summary>Terrain block data messages.</summary>
    public static Gen<TerrainBlockData> TerrainBlockData { get; } =
        from key in TerrainKey
        from version in VarU32
        from data in Bytes(ProtocolConstants.MaxTerrainBlockDataBytes)
        select new TerrainBlockData(key, version, data);

    /// <summary>Terrain block blobs (envelope fields; the voxel payload is opaque to Core). The 7 below is
    /// <c>StateCodec.TerrainBlockHeaderBytes</c> (private); <c>TerrainBlockBlobTests</c> pins it.</summary>
    public static Gen<TerrainBlockBlob> TerrainBlockBlob { get; } =
        from voxelFormat in Gen.Choose(1, 255)
        from voxels in Bytes(ProtocolConstants.MaxTerrainBlockDataBytes - 7)
        select new TerrainBlockBlob((byte)voxelFormat, voxels);

    /// <summary>Terrain checksum messages.</summary>
    public static Gen<TerrainChecksums> TerrainChecksums { get; } =
        from planet in PlanetId
        from entries in ListUpTo(TerrainChecksum, ProtocolConstants.MaxTerrainChecksumEntries, 10)
        select new TerrainChecksums(planet, entries);

    /// <summary>Construct state parts with 0 to 12 distinct chunks.</summary>
    public static Gen<ConstructState> ConstructState { get; } =
        from header in ConstructHeader
        from count in Gen.Frequency((3, Gen.Choose(0, 2)), (1, Gen.Choose(0, ProtocolConstants.MaxChunksPerConstructStatePart)))
        from positions in DistinctChunks(count)
        from chunks in Gen.CollectToArray(positions.Select(p => ChunkBlocks.Select(b => new ConstructChunkRecord(new ConstructChunkKey(header.Id, p), b))))
        from partCount in Gen.Choose(1, ushort.MaxValue)
        from partIndex in Gen.Choose(0, partCount - 1)
        select new ConstructState(header, (ushort)partIndex, (ushort)partCount, chunks);

    /// <summary>Construct removed messages.</summary>
    public static Gen<ConstructRemoved> ConstructRemoved { get; } = EntityId.Select(e => new ConstructRemoved(e));

    /// <summary>Operation applied messages; the construct changes are strictly increasing by entity id.</summary>
    public static Gen<OperationApplied> OperationApplied { get; } =
        from id in OpId
        from tick in Tick
        from op in Operation
        from changes in SortedChanges()
        select new OperationApplied(id, tick, op, changes);

    /// <summary>Intent submit messages.</summary>
    public static Gen<IntentSubmit> IntentSubmit { get; } =
        from seq in VarU32
        from intent in Intent
        select new IntentSubmit(seq, intent);

    /// <summary>Intent reply messages.</summary>
    public static Gen<IntentReply> IntentReply { get; } =
        from seq in VarU32
        from reason in Gen.Elements(Enumerable.Range(0, 22).Append(30).Append(31).Append(37).ToArray())
        from op in OpId
        select new IntentReply(seq, (RejectReason)reason, op);

    /// <summary>Player inputs with axes in [-1, 1] (before quantization).</summary>
    public static Gen<PlayerInput> PlayerInput { get; } =
        from tick in VarU32
        from x in UnitRange
        from y in UnitRange
        from yaw in Finite
        from pitch in Finite
        from buttons in Gen.Choose(0, ProtocolConstants.InputButtonsMask)
        select new PlayerInput(tick, x, y, yaw, pitch, (InputButtons)buttons);

    /// <summary>Input batches with 1 to 5 inputs.</summary>
    public static Gen<InputBatch> InputBatch { get; } =
        from seq in VarU32
        from count in Gen.Choose(1, ProtocolConstants.MaxInputsPerBatch)
        from inputs in PlayerInput.ArrayOf(count)
        select new InputBatch(seq, inputs);

    /// <summary>Snapshots with 1 to 8 distinct frames and 0 to 22 entity states.</summary>
    public static Gen<Snapshot> Snapshot { get; } =
        from id in VarU32
        from tick in Tick
        from baseline in VarU32
        from lastInput in VarU32
        from frameCount in Gen.Choose(1, ProtocolConstants.MaxFramesPerSnapshot)
        from frames in DistinctFrames(frameCount)
        from entityCount in Gen.Frequency((3, Gen.Choose(0, 5)), (1, Gen.Choose(0, ProtocolConstants.MaxEntitiesPerSnapshot)))
        from entities in State(frameCount).ArrayOf(entityCount)
        select new Snapshot(id, tick, baseline, lastInput, frames, entities);

    /// <summary>Snapshot acknowledgements.</summary>
    public static Gen<SnapshotAck> SnapshotAck { get; } = VarU32.Select(v => new SnapshotAck(v));

    /// <summary>Entity spawn messages; the initial state uses frame index 0.</summary>
    public static Gen<EntitySpawn> EntitySpawn { get; } =
        from entity in EntityId
        from kind in Gen.Elements(EntityKind.Player, EntityKind.Construct)
        from archetype in VarU16
        from owner in Optional(PlayerId)
        from frame in SnapshotFrame
        from initial in State(1)
        from data in Bytes(ProtocolConstants.MaxEntityKindDataBytes)
        select new EntitySpawn(entity, kind, archetype, owner, frame, initial, data);

    /// <summary>Entity despawn messages.</summary>
    public static Gen<EntityDespawn> EntityDespawn { get; } = EntityId.Select(e => new EntityDespawn(e));

    /// <summary>Ping messages.</summary>
    public static Gen<Ping> Ping { get; } = Full64.Select(v => new Ping(v));

    /// <summary>Pong messages.</summary>
    public static Gen<Pong> Pong { get; } = Full64.SelectMany(v => Tick.Select(t => new Pong(v, t)));

    /// <summary>Disconnect messages.</summary>
    public static Gen<Disconnect> Disconnect { get; } = Gen.Choose(0, 6).Select(c => new Disconnect((DisconnectCode)c));

    /// <summary>Lists with up to <paramref name="max"/> elements, usually at most <paramref name="typical"/>.</summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Element generator.</param>
    /// <param name="max">Hard limit.</param>
    /// <param name="typical">Usual upper bound.</param>
    /// <returns>The generator.</returns>
    public static Gen<IReadOnlyList<T>> ListUpTo<T>(Gen<T> element, int max, int typical) =>
        Gen.Frequency((6, Gen.Choose(0, Math.Min(typical, max))), (1, Gen.Choose(0, max)))
        .SelectMany(n => element.ArrayOf(n).Select(a => (IReadOnlyList<T>)a));

    private static Gen<ChunkPos[]> DistinctChunks(int count) =>
        Chunk.ArrayOf(count * 2 + 4).Select(all => all.Distinct().Take(count).ToArray());

    private static Gen<SnapshotFrame[]> DistinctFrames(int count) =>
        SnapshotFrame.ArrayOf(count * 2 + 4).Select(all => all.DistinctBy(f => f.Frame).Take(count).ToArray());

    private static Gen<IReadOnlyList<ConstructChange>> SortedChanges() =>
        Gen.Frequency((6, Gen.Choose(0, 6)), (1, Gen.Choose(0, 200)))
        .SelectMany(n => EntityId.SelectMany(e => VarU32.SelectMany(v => Gen.Elements(true, false).Select(r => new ConstructChange(e, v, r)))).ArrayOf(n))
        .Select(all => (IReadOnlyList<ConstructChange>)all.DistinctBy(c => c.Construct).OrderBy(c => c.Construct.Value).ToArray());

    private static EntityState MakeState(
        EntityId entity, EntityStateFields fields, byte frameIndex, uint parentSeq, Vector3 position, Quaternion rotation, Vector3 velocity, byte motion) =>
        new(
            entity,
            fields,
            frameIndex,
            parentSeq,
            fields.HasFlag(EntityStateFields.Position) ? position : default,
            fields.HasFlag(EntityStateFields.Rotation) ? rotation : Quaternion.Identity,
            fields.HasFlag(EntityStateFields.Velocity) ? velocity : default,
            fields.HasFlag(EntityStateFields.MotionFlags) ? motion : (byte)0);

    private static Gen<uint[]> BuildChunk(int shape, int seed)
    {
        var rng = new Random(seed);
        var blocks = new uint[ChunkCells];
        switch (shape)
        {
            case 0:
                break;
            case 1:
                Array.Fill(blocks, RandomBlock(rng));
                break;
            case 2:
                for (var i = rng.Next(1, 40); i > 0; i--)
                {
                    blocks[rng.Next(ChunkCells)] = RandomBlock(rng);
                }

                break;
            case 3:
                for (var i = 0; i < ChunkCells;)
                {
                    var length = rng.Next(1, 600);
                    var value = rng.Next(3) == 0 ? 0u : RandomBlock(rng);
                    for (var j = 0; j < length && i < ChunkCells; j++)
                    {
                        blocks[i++] = value;
                    }
                }

                break;
            case 4:
                // Worst case for run-length encoding: no two neighbours are equal, so there are 4096 runs.
                var a = RandomBlock(rng);
                var b = BlockState.Create(2, (byte)rng.Next(BlockState.OrientationCount)).Packed;
                for (var i = 0; i < ChunkCells; i++)
                {
                    blocks[i] = (i & 1) == 0 ? a : b;
                }

                break;
            default:
                for (var i = 0; i < ChunkCells; i++)
                {
                    blocks[i] = rng.Next(4) == 0 ? 0u : RandomBlock(rng);
                }

                break;
        }

        return Gen.Constant(blocks);
    }

    private static uint RandomBlock(Random rng) =>
        BlockState.Create(
            (ushort)rng.Next(1, ushort.MaxValue + 1),
            (byte)rng.Next(BlockState.OrientationCount),
            (byte)rng.Next(BlockState.CostRevisionCount),
            (byte)rng.Next(BlockState.FunctionStateCount)).Packed;

    private static string FitText(string[] pieces, int minBytes, int maxBytes)
    {
        var sb = new StringBuilder();
        var bytes = 0;
        foreach (var piece in pieces)
        {
            var size = Encoding.UTF8.GetByteCount(piece);
            if (bytes + size > maxBytes)
            {
                continue;
            }

            sb.Append(piece);
            bytes += size;
        }

        while (bytes < minBytes)
        {
            sb.Append('a');
            bytes++;
        }

        return sb.ToString();
    }

    private static Quaternion Normalize(Quaternion q)
    {
        var length = q.Length();
        return length < 1e-6f || !float.IsFinite(length) ? Quaternion.Identity : Quaternion.Normalize(q);
    }

    private static Gen<TResult> Gen3<T1, T2, T3, TResult>(Gen<T1> a, Gen<T2> b, Gen<T3> c, Func<T1, T2, T3, TResult> make) =>
        from x in a
        from y in b
        from z in c
        select make(x, y, z);

    private static Gen<TResult> Gen4<T1, T2, T3, T4, TResult>(Gen<T1> a, Gen<T2> b, Gen<T3> c, Gen<T4> d, Func<T1, T2, T3, T4, TResult> make) =>
        from x in a
        from y in b
        from z in c
        from w in d
        select make(x, y, z, w);
}
