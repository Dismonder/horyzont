using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>The largest valid message of every type: full lists, longest strings, maximal varuint values, worst-case run-length encoding.</summary>
public static class MaxMessages
{
    /// <summary>A string of exactly <paramref name="bytes"/> UTF-8 bytes that mixes 1-byte and 3-byte characters.</summary>
    /// <param name="bytes">Encoded length.</param>
    /// <returns>The string.</returns>
    public static string Text(int bytes) => new string('€', bytes / 3) + new string('a', bytes % 3);

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
            new ShipInputBatch(
                uint.MaxValue,
                Enumerable.Range(0, ProtocolConstants.MaxInputsPerBatch)
                    .Select(_ => new ShipInput(uint.MaxValue, new ShipControl(-127, 127, -127, 127, -127, 127, (ShipButtons)7)))
                    .ToArray()),
            new ShipCorrection(maxEntity, uint.MaxValue, uint.MaxValue, new Vector3(float.MaxValue), Quaternion.Identity, new Vector3(float.MinValue),
                new Vector3(float.MaxValue), float.MaxValue, (ShipFlightFlags)255, ShipTerrainGate.Unavailable, ShipLaunchBlock.PersonInHull, float.MaxValue),
        ];
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
