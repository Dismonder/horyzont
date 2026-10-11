using System.Buffers.Binary;
using System.Numerics;

using Horyzont.Bots.Digests;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;
using Horyzont.Server;
using Horyzont.Server.Replication;
using Horyzont.Server.Terrain;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Integration.Tests.Coop;

/// <summary>
/// The server side of the replication of K3-15 and K3-16 in the co-op rig: entity and construct replication through the
/// <see cref="EntityReplicationBinder"/> over the source of the game (linked file), and the <see cref="ServerTerrainReplicator"/> over the
/// fake terrain, both drawing on one reliable budget, as the game composes them.
/// </summary>
internal sealed class CoopReplication : IServerTickParticipant, IServerPlayerObserver, IServerClientMessageHandler
{
    private readonly CoopCharacters _characters;
    private readonly TimeProvider _time;
    private ServerRuntime? _runtime;

    public CoopReplication(CoopCharacters characters, TimeProvider time)
    {
        _characters = characters;
        _time = time;
        Entities = new EntityReplicationBinder(
            runtime => new Horyzont.Game.Server.Replication.ServerReplicationSource(runtime.State, characters), time, NullLoggerFactory.Instance,
            new MotionSnapshotBudget(time));
    }

    public EntityReplicationBinder Entities { get; }

    public ServerTerrainReplicator? Terrain { get; private set; }

    public void Register(ServerRuntime runtime)
    {
        _runtime = runtime;
        Entities.Register(runtime);
        runtime.AddTickParticipant(this);
        runtime.AddPlayerObserver(this);
        runtime.AddClientMessageHandler(this);
    }

    public void OnTick(ServerRuntime runtime) => Ensure().OnTick(runtime);

    public void OnShutdown(ServerRuntime runtime) => Terrain?.OnShutdown(runtime);

    public void OnPlayerReady(PlayerId player) => Ensure().OnPlayerReady(player);

    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason) => Terrain?.OnPlayerLeaving(player, reason);

    public bool Handle(PlayerId player, Message message) =>
        message is TerrainCacheReport or TerrainChecksums or TerrainView && Ensure().Handle(player, message);

    private ServerTerrainReplicator Ensure()
    {
        if (Terrain is not null)
        {
            return Terrain;
        }

        var runtime = _runtime ?? throw new InvalidOperationException("Not registered.");
        Terrain = new ServerTerrainReplicator(
            new Host(runtime, _characters), new SyntheticTerrainSource(runtime), runtime.State, Entities.SendBudget, _time,
            NullLogger.Instance);
        runtime.Simulation.Applied += Terrain.OnOperationApplied;
        return Terrain;
    }

    private sealed class Host(ServerRuntime runtime, CoopCharacters characters) : ITerrainReplicationHost
    {
        public bool FreeCameraAllowed => false;

        public bool IsLocalPlayer(PlayerId player) => runtime.IsLocalPlayer(player);

        public bool TryGetFeet(PlayerId player, out PlanetId planet, out Vector3 feet)
        {
            if (characters.TryGetState(player, out var state))
            {
                planet = state.Planet;
                feet = state.Feet;
                return true;
            }

            planet = default;
            feet = default;
            return false;
        }

        public void Send(PlayerId player, Message message) => runtime.Session.Send(player, message);
    }
}

/// <summary>
/// Envelopes of the fake terrain: a block has no voxels here, so its envelope is a deterministic function of (key, version). That is
/// enough for the protocol (manifests, hashes, caches) and survives restarts, because the version table is persisted.
/// </summary>
/// <param name="runtime">The server.</param>
internal sealed class SyntheticTerrainSource(ServerRuntime runtime) : ITerrainReplicationSource
{
    public static byte[] Envelope(TerrainBlockKey key, uint version)
    {
        var blob = new byte[25];
        BinaryPrimitives.WriteUInt64LittleEndian(blob, key.Planet.Value);
        blob[8] = key.Lod;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(9), key.X);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(13), key.Y);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(17), key.Z);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(21), version);
        return blob;
    }

    public bool TryExportBlock(TerrainBlockKey key, out uint version, out byte[] blob)
    {
        version = runtime.State.TerrainVersions.Get(key);
        blob = Envelope(key, version);
        return key.Lod == 0;
    }

    public bool TryGetGeneratedDigest(TerrainBlockKey key, out ulong digest)
    {
        digest = 0;
        return false;
    }
}

/// <summary>The digest entries of the server of the rig, as the game's <c>ServerStateDigestSource</c> writes them (without voxels).</summary>
internal static class CoopServerDigest
{
    public static List<DigestEntry> Entries(ServerRuntime runtime)
    {
        var entries = runtime.State.Constructs.All.Select(ConstructDigest.Entry).ToList();
        foreach (var (key, version) in runtime.State.TerrainVersions.All)
        {
            entries.Add(TerrainVersionDigest.Entry(key, version, TerrainBlockHash.Compute(SyntheticTerrainSource.Envelope(key, version))));
        }

        return entries;
    }
}
