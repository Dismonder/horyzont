using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Encoding invalid values is a bug in our own code, so every writer throws <see cref="ArgumentException"/> (never produces bytes that the
/// reader would reject).
/// </summary>
public sealed class WriterValidationTests
{
    private static readonly FrameRef _frame = new(FrameKind.Planet, 9);
    private static readonly EntityId _entity = new(5);
    private static readonly PlatformIdentity _identity = new(IdentityPlatform.Steam, 1);
    private static readonly Quaternion _bad = new(2, 0, 0, 0);

    private static EntityState State(byte frameIndex = 0, EntityStateFields fields = EntityStateFields.None, Vector3 velocity = default, Quaternion? rotation = null) =>
        new(new EntityId(1), fields, frameIndex, 0, default, rotation ?? Quaternion.Identity, velocity, 0);

    private static Snapshot Snap(IReadOnlyList<SnapshotFrame> frames, IReadOnlyList<EntityState> entities) => new(1, new Tick(1), 0, 0, frames, entities);

    private static SnapshotFrame[] Frames(int count) => Enumerable.Range(1, count).Select(i => new SnapshotFrame(new FrameRef(FrameKind.Planet, (ulong)i), 0)).ToArray();

    private static ConstructHeaderRecord Header(EntityId? id = null) =>
        new(id ?? _entity, _frame, 0, ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 1);

    private static ConstructChunkRecord Chunk(int x, EntityId? construct = null, int blocks = 4096) =>
        new(new ConstructChunkKey(construct ?? _entity, new ChunkPos(x, 0, 0)), new uint[blocks]);

    private static void Rejects(string what, Action write)
    {
        var ex = Record.Exception(write);

        Assert.True(ex is ArgumentException, $"{what}: expected ArgumentException but got {ex?.GetType().Name ?? "no exception"}: {ex?.Message}");
    }

    private static void RejectsMessage(string what, Message message) => Rejects(what, () => Kit.Encode(message));

    private static void RejectsOperation(string what, Operation op) => Rejects(what, () => Kit.Encode(op));

    private static void RejectsIntent(string what, Intent intent) => Rejects(what, () => Kit.Encode(intent));

    [Fact]
    public void Write_NonFiniteFloatsInOperationsAndIntents_ThrowArgumentException()
    {
        var nan = float.NaN;
        var inf = float.PositiveInfinity;
        var ok = new Vector3(1, 2, 3);

        RejectsOperation("TerrainEdit center", new TerrainEditOp(new PlanetId(1), TerrainShape.Sphere, new Vector3(nan, 0, 0), 1, TerrainEditMode.Add, 1));
        RejectsOperation("TerrainEdit radius", new TerrainEditOp(new PlanetId(1), TerrainShape.Sphere, ok, inf, TerrainEditMode.Add, 1));
        RejectsOperation("ConstructCreate origin", new ConstructCreateOp(_entity, _frame, new Vector3(0, -inf, 0), Quaternion.Identity, default, BlockState.Create(1, 0)));
        RejectsOperation("EntityReparent velocity", new EntityReparentOp(_entity, _frame, 0, ok, Quaternion.Identity, new Vector3(0, 0, nan), null));
        RejectsOperation("PlayerLeave pitch", new PlayerLeaveOp(new PlayerId(1), _frame, 0, ok, Quaternion.Identity, nan));
        RejectsOperation("PlayerJoin position", new PlayerJoinOp(new PlayerId(1), _identity, "a", _entity, _frame, 0, new Vector3(inf, 0, 0), Quaternion.Identity));
        RejectsIntent("TerrainTool origin", new TerrainToolIntent(new PlanetId(1), new Vector3(nan, 0, 0), ok, 1, TerrainEditMode.Add, 1));
        RejectsIntent("TerrainTool radius", new TerrainToolIntent(new PlanetId(1), ok, ok, nan, TerrainEditMode.Add, 1));
        RejectsIntent("PlaceBlock terrain look", new PlaceBlockIntent(new TerrainTarget(new PlanetId(1), ok, ok, new Vector3(0, inf, 0)), 1, 0));
    }

    [Fact]
    public void Write_NonUnitRotationsInOperationsHeadersAndRecords_ThrowArgumentException()
    {
        RejectsOperation("ConstructCreate rotation", new ConstructCreateOp(_entity, _frame, default, _bad, default, BlockState.Create(1, 0)));
        RejectsOperation("EntityReparent rotation", new EntityReparentOp(_entity, _frame, 0, default, Quaternion.Identity * 0f, default, null));
        RejectsOperation("PlayerJoin rotation", new PlayerJoinOp(new PlayerId(1), _identity, "a", _entity, _frame, 0, default, _bad));
        RejectsOperation("PlayerLeave rotation", new PlayerLeaveOp(new PlayerId(1), _frame, 0, default, _bad, 0));
        Rejects("header rotation", () => Kit.Write(w => StateCodec.WriteConstructHeader(w, Header() with { Rotation = _bad })));
        Rejects(
            "player rotation",
            () => Kit.Write(w => StateCodec.WritePlayer(w, new PlayerRecord(new PlayerId(1), _identity, "a", _entity, _frame, 0, null, default, _bad, 0, true))));
        RejectsMessage("snapshot entity rotation", Snap(Frames(1), [State(fields: EntityStateFields.Rotation, rotation: _bad)]));
    }

    [Fact]
    public void Write_UndefinedEnumValues_ThrowArgumentException()
    {
        RejectsIntent("Face", new PlaceBlockIntent(new ConstructFaceTarget(_entity, default, (Face)9), 1, 0));
        RejectsOperation("TerrainEditMode", new TerrainEditOp(new PlanetId(1), TerrainShape.Sphere, default, 1, (TerrainEditMode)0, 1));
        RejectsOperation("TerrainShape", new TerrainEditOp(new PlanetId(1), (TerrainShape)7, default, 1, TerrainEditMode.Add, 1));
        RejectsOperation("FrameKind", new PlayerLeaveOp(new PlayerId(1), new FrameRef((FrameKind)0, 1), 0, default, Quaternion.Identity, 0));
        RejectsOperation("IdentityPlatform", new PlayerJoinOp(new PlayerId(1), new PlatformIdentity((IdentityPlatform)9, 1), "a", _entity, _frame, 0, default, Quaternion.Identity));
        RejectsMessage("DisconnectCode", new Disconnect((DisconnectCode)99));
        RejectsMessage("RejectReason", new IntentReply(1, (RejectReason)99, new OpId(1)));
        RejectsMessage("EntityKind", new EntitySpawn(_entity, (EntityKind)9, 1, null, new SnapshotFrame(_frame, 0), State(), []));
        RejectsMessage("InputButtons", new InputBatch(1, [new PlayerInput(0, 0, 0, 0, 0, (InputButtons)0x20)]));
        RejectsMessage("EntityStateFields", Snap(Frames(1), [State(fields: (EntityStateFields)0x10)]));
        Rejects("ConstructMode", () => Kit.Write(w => StateCodec.WriteConstructHeader(w, Header() with { Mode = (ConstructMode)5 })));
    }

    [Fact]
    public void Write_MalformedBlockState_ThrowsArgumentException()
    {
        RejectsOperation("reserved bits", new BlockSetOp(_entity, default, new BlockState(0x10000001)));
        RejectsOperation("type 0 with payload", new BlockSetOp(_entity, default, new BlockState(0x00030000)));
        RejectsOperation("orientation 24", new ConstructCreateOp(_entity, _frame, default, Quaternion.Identity, default, new BlockState(0x00180001)));
    }

    [Fact]
    public void Write_TooLongStrings_ThrowArgumentException()
    {
        var okHello = new ClientHello(1, 1, _identity, "a", "b", []);

        RejectsMessage("DisplayName", okHello with { DisplayName = new string('x', 33) });
        RejectsMessage("Password", okHello with { Password = new string('x', 65) });
        RejectsMessage("AuthTicket", okHello with { AuthTicket = new byte[1025] });
        RejectsMessage("Detail", new HandshakeReject(HandshakeRejectCode.Banned, new string('x', 257)));
        RejectsOperation("PlayerJoin name", new PlayerJoinOp(new PlayerId(1), _identity, new string('x', 33), _entity, _frame, 0, default, Quaternion.Identity));
        RejectsMessage("KindData", new EntitySpawn(_entity, EntityKind.Player, 1, null, new SnapshotFrame(_frame, 0), State(), new byte[65]));
        RejectsMessage("TerrainBlockData", new TerrainBlockData(default, 1, new byte[ProtocolConstants.MaxTerrainBlockDataBytes + 1]));
        Rejects("lone surrogate", () => Kit.Write(w => w.WriteString("\uD800", 8)));
    }

    [Fact]
    public void Write_ListsAboveTheirLimits_ThrowArgumentException()
    {
        var planet = new PlanetParams(new PlanetId(1), new SystemId(1), 1, "p");
        var version = new TerrainBlockVersion(default, 1);

        RejectsMessage("InputBatch 0", new InputBatch(1, []));
        RejectsMessage("InputBatch 6", new InputBatch(1, Enumerable.Repeat(default(PlayerInput), 6).ToArray()));
        RejectsMessage("Snapshot frames 0", Snap([], []));
        RejectsMessage("Snapshot frames 9", Snap(Frames(9), []));
        RejectsMessage("Snapshot entities 23", Snap(Frames(1), Enumerable.Repeat(State(), 23).ToArray()));
        RejectsMessage("ServerHello planets 65", new ServerHello(default, default, 20, new PlayerId(1), _entity, Enumerable.Repeat(planet, 65).ToArray(), [], []));
        RejectsMessage("ServerHello blocks 2049", new ServerHello(default, default, 20, new PlayerId(1), _entity, [], Enumerable.Repeat(new RegistryEntry(1, "k"), 2049).ToArray(), []));
        RejectsMessage("ServerHello materials 256", new ServerHello(default, default, 20, new PlayerId(1), _entity, [], [], Enumerable.Repeat(new RegistryEntry(1, "k"), 256).ToArray()));
        RejectsMessage("TerrainCacheReport 4097", new TerrainCacheReport(new PlanetId(1), Enumerable.Repeat(version, 4097).ToArray()));
        RejectsMessage("TerrainChecksums 1025", new TerrainChecksums(new PlanetId(1), Enumerable.Repeat(default(TerrainBlockChecksum), 1025).ToArray()));
        RejectsMessage(
            "ConstructState chunks 13",
            new ConstructState(Header(), 0, 1, Enumerable.Range(0, 13).Select(i => Chunk(i)).ToArray()));
        RejectsMessage(
            "OperationApplied changes 1025",
            new OperationApplied(new OpId(1), new Tick(1), new BlockClearOp(_entity, default), Enumerable.Range(1, 1025).Select(i => new ConstructChange(new EntityId((ulong)i), 1, false)).ToArray()));
    }

    [Fact]
    public void Write_SnapshotInvariants_ThrowArgumentException()
    {
        RejectsMessage("duplicate frames", Snap([new SnapshotFrame(_frame, 0), new SnapshotFrame(_frame, 0)], []));
        RejectsMessage("FrameIndex outside table", Snap(Frames(2), [State(frameIndex: 2)]));
        RejectsMessage("velocity beyond half", Snap(Frames(1), [State(fields: EntityStateFields.Velocity, velocity: new Vector3(70000, 0, 0))]));
        RejectsMessage("velocity NaN", Snap(Frames(1), [State(fields: EntityStateFields.Velocity, velocity: new Vector3(0, float.NaN, 0))]));
    }

    [Fact]
    public void Write_EntitySpawnInitialStateMustUseFrameIndexZero() =>
        RejectsMessage("Initial.FrameIndex", new EntitySpawn(_entity, EntityKind.Player, 1, null, new SnapshotFrame(_frame, 0), State(frameIndex: 1), []));

    [Fact]
    public void Write_ConstructStateInvariants_ThrowArgumentException()
    {
        RejectsMessage("PartCount 0", new ConstructState(Header(), 0, 0, []));
        RejectsMessage("PartIndex >= PartCount", new ConstructState(Header(), 2, 2, []));
        RejectsMessage("duplicate chunk position", new ConstructState(Header(), 0, 1, [Chunk(0), Chunk(0)]));
        RejectsMessage("chunk of another construct", new ConstructState(Header(), 0, 1, [Chunk(0, new EntityId(6))]));
        RejectsMessage("chunk with short block array", new ConstructState(Header(), 0, 1, [Chunk(0, blocks: 100)]));
    }

    [Fact]
    public void Write_OperationAppliedChangesMustBeStrictlyAscending()
    {
        var op = new BlockClearOp(_entity, default);

        RejectsMessage("descending", new OperationApplied(new OpId(1), new Tick(1), op, [new ConstructChange(new EntityId(6), 1, false), new ConstructChange(new EntityId(5), 1, false)]));
        RejectsMessage("duplicate", new OperationApplied(new OpId(1), new Tick(1), op, [new ConstructChange(new EntityId(5), 1, false), new ConstructChange(new EntityId(5), 2, true)]));
    }

    [Fact]
    public void Write_ServerHelloRegistryEntries_AreRangeChecked()
    {
        RejectsMessage("block id 0", new ServerHello(default, default, 20, new PlayerId(1), _entity, [], [new RegistryEntry(0, "k")], []));
        RejectsMessage("block id 2049", new ServerHello(default, default, 20, new PlayerId(1), _entity, [], [new RegistryEntry(2049, "k")], []));
        RejectsMessage("material id 256", new ServerHello(default, default, 20, new PlayerId(1), _entity, [], [], [new RegistryEntry(256, "k")]));
        RejectsMessage("key 65 bytes", new ServerHello(default, default, 20, new PlayerId(1), _entity, [], [new RegistryEntry(1, new string('k', 65))], []));
    }

    [Fact]
    public void Write_OversizedFrames_ThrowArgumentExceptionBeforeAnythingIsSent()
    {
        // Past every list limit these frames are also over the channel limit; the plan requires an ArgumentException either way.
        RejectsMessage("snapshot over 1150 bytes", Snap(Frames(8), Enumerable.Repeat(State(fields: (EntityStateFields)0x0F, velocity: new Vector3(1, 2, 3)), 40).ToArray()));
        RejectsMessage("reliable frame over 256 KiB", new ConstructState(Header(), 0, 1, Enumerable.Range(0, 13).Select(i => WorstChunk(i)).ToArray()));
    }

    private static ConstructChunkRecord WorstChunk(int x)
    {
        var blocks = new uint[4096];
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = BlockState.Create((ushort)(1 + (i & 1)), 0).Packed;
        }

        return new ConstructChunkRecord(new ConstructChunkKey(_entity, new ChunkPos(x, 0, 0)), blocks);
    }
}
