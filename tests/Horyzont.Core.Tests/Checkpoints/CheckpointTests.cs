using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Checkpoints;

public sealed class CheckpointTests
{
    [Fact]
    public void Capture_SecondWithoutChanges_ContainsOnlyOnlinePlayers()
    {
        var f = new SimFixture();
        var a = f.Join(1);
        f.Join(2);
        f.BuildOnTerrain(a, 2);
        f.Sim.Submit(a, f.Dig(a, f.Ground(-2)));
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(a, f.PlayerOf(a).Frame, 0, f.PlayerOf(a).Position, Quaternion.Identity, 0));
        var first = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(first);

        var second = f.State.CaptureCheckpoint();

        Assert.NotEmpty(first.UpsertedConstructs);
        Assert.NotEmpty(first.TerrainVersions);
        Assert.Equal(2, first.UpsertedPlayers.Count);
        Assert.Empty(second.UpsertedConstructs);
        Assert.Empty(second.RemovedConstructs);
        Assert.Empty(second.UpsertedChunks);
        Assert.Empty(second.RemovedChunks);
        Assert.Empty(second.TerrainVersions);
        Assert.Equal([new PlayerId(2)], second.UpsertedPlayers.Select(p => p.Player));
        Assert.Equal(f.State.LastOpId, second.BarrierOpId);
    }

    [Fact]
    public void Capture_InFlight_SecondCaptureThrowsUntilClosed()
    {
        var f = new SimFixture();
        f.Join();

        var checkpoint = f.State.CaptureCheckpoint();

        Assert.True(f.State.IsCheckpointInFlight);
        Assert.Throws<InvalidOperationException>(() => f.State.CaptureCheckpoint());
        Assert.Throws<InvalidOperationException>(() => f.State.CompleteCheckpoint(checkpoint with { }));
        f.State.CompleteCheckpoint(checkpoint);
        Assert.False(f.State.IsCheckpointInFlight);
        var next = f.State.CaptureCheckpoint();
        f.State.RequeueAfterFailedCheckpoint(next);
        Assert.False(f.State.IsCheckpointInFlight);
    }

    [Fact]
    public void Requeue_RestoresUpsertsAndRemovals()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);
        var failed = f.State.CaptureCheckpoint();
        f.State.RequeueAfterFailedCheckpoint(failed);

        var retry = f.State.CaptureCheckpoint();

        Assert.Equal(failed.UpsertedConstructs, retry.UpsertedConstructs);
        Assert.Equal(failed.UpsertedChunks.Select(x => x.Key), retry.UpsertedChunks.Select(x => x.Key));
        Assert.Equal(failed.UpsertedPlayers, retry.UpsertedPlayers);
        f.State.CompleteCheckpoint(retry);

        Assert.True(f.Sim.Submit(p, new RemoveBlockIntent(c, new CellPos(0, 0, 0))).IsAccepted);
        var removal = f.State.CaptureCheckpoint();
        Assert.Equal([c], removal.RemovedConstructs);
        Assert.Single(removal.RemovedChunks);
        f.State.RequeueAfterFailedCheckpoint(removal);

        var retryRemoval = f.State.CaptureCheckpoint();

        Assert.Equal([c], retryRemoval.RemovedConstructs);
        Assert.Equal(removal.RemovedChunks, retryRemoval.RemovedChunks);
        Assert.Empty(retryRemoval.UpsertedConstructs);
    }

    [Fact]
    public void Requeue_ChunkRemovedThenRefilled_NextIsUpsertNotRemoval()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);
        f.State.CompleteCheckpoint(f.State.CaptureCheckpoint());
        var high = new CellPos(0, 16, 0);
        f.SetBlock(c, high);
        f.State.CompleteCheckpoint(f.State.CaptureCheckpoint());
        f.Sim.ApplyServerOperation(new BlockClearOp(c, high));
        var failed = f.State.CaptureCheckpoint();
        var key = new ConstructChunkKey(c, high.Chunk);
        Assert.Equal([key], failed.RemovedChunks);
        f.State.RequeueAfterFailedCheckpoint(failed);
        f.SetBlock(c, high);

        var next = f.State.CaptureCheckpoint();

        Assert.Empty(next.RemovedChunks);
        var chunk = Assert.Single(next.UpsertedChunks);
        Assert.Equal(key, chunk.Key);
        Assert.Equal(4096, chunk.Blocks.Length);
        Assert.Equal(BlockState.Create(f.Stone, 0).Packed, chunk.Blocks[high.LocalIndex]);
    }

    [Fact]
    public void Builder_CheckpointsPlusTail_SameHashAsLive()
    {
        var f = new SimFixture();
        var a = f.Join(1, "alpha");
        var b = f.Join(2, "beta");
        var c = f.BuildOnTerrain(a, 2);
        f.Sim.Submit(a, f.Dig(a, f.Ground(-2)));
        var checkpoints = new List<WorldCheckpoint>();
        checkpoints.Add(f.State.CaptureCheckpoint());
        f.State.CompleteCheckpoint(checkpoints[^1]);

        f.Ticks(7);
        f.Sim.Submit(a, new PlaceBlockIntent(new ConstructFaceTarget(c, new CellPos(0, 0, 0), Face.PosY), f.Stone, 2));
        f.BuildOnTerrain(b, 0, 3);
        checkpoints.Add(f.State.CaptureCheckpoint());
        f.State.CompleteCheckpoint(checkpoints[^1]);

        f.Ticks(7);
        f.Sim.Submit(a, new RemoveBlockIntent(c, new CellPos(0, 1, 0)));
        f.Sim.Submit(b, f.Fill(b, f.Ground(0, -4)));
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(a, f.PlayerOf(a).Frame, 0, f.PlayerOf(a).Position, Quaternion.Identity, 0.5f));
        var barrier = checkpoints[^1].BarrierOpId;
        var tail = f.Journal.Entries.Where(e => e.Id > barrier).ToList();
        Assert.Contains(tail, e => e.Operation is PlayerLeaveOp);
        Assert.True(checkpoints[^1].UpsertedPlayers.Single(r => r.Player == a).IsOnline);

        var loaded = Load(f.State, checkpoints);
        var replay = new SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
        Assert.Equal(f.State.LastOpId, loaded.LastOpId);
    }

    [Fact]
    public void Builder_InconsistentData_Throws()
    {
        var builder = new WorldStateBuilder(new WorldId(Guid.NewGuid()), 1);
        Assert.Throws<InvalidDataException>(() => builder.AddChunk(new ConstructChunkRecord(new ConstructChunkKey(new EntityId(1), default), new uint[4096])));

        var noBlocks = new WorldStateBuilder(new WorldId(Guid.NewGuid()), 1);
        noBlocks.SetProgress(OpId.None, default, 2, 1);
        noBlocks.AddPlanet(Testing.WorldFactory.HomePlanetParams);
        noBlocks.AddConstruct(new ConstructHeaderRecord(new EntityId(1), FrameRef.Of(SimFixture.Planet), 0, ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 1));
        Assert.Throws<InvalidDataException>(() => noBlocks.Build());

        var missingFrame = new WorldStateBuilder(new WorldId(Guid.NewGuid()), 1);
        missingFrame.SetProgress(OpId.None, default, 2, 2);
        missingFrame.AddPlanet(Testing.WorldFactory.HomePlanetParams);
        missingFrame.AddPlayer(new PlayerRecord(
            new PlayerId(1), new PlatformIdentity(IdentityPlatform.Dev, 1), "x", new EntityId(1), FrameRef.Of(new EntityId(9)), 0, null,
            Vector3.Zero, Quaternion.Identity, 0, false));
        Assert.Throws<InvalidDataException>(() => missingFrame.Build());
    }

    [Theory]
    [InlineData(128, 0, 0)]
    [InlineData(-129, 0, 0)]
    [InlineData(0, 128, 0)]
    [InlineData(0, 0, -129)]
    [InlineData(1 << 28, 0, 0)]
    public void Builder_ChunkOutsideCellRange_Throws(int x, int y, int z)
    {
        var builder = ValidBuilder(Quaternion.Identity, new ChunkPos(x, y, z));

        Assert.Throws<InvalidDataException>(() => builder.Build());
    }

    [Fact]
    public void Builder_ChunkAtRangeEdge_Loads()
    {
        var state = ValidBuilder(Quaternion.Identity, new ChunkPos(127, -128, 127)).Build();

        Assert.True(state.Constructs.TryGet(new EntityId(1), out var construct));
        Assert.Equal(1, construct.BlockCount);
    }

    [Fact]
    public void Builder_ConstructRotationNotNormalized_Throws()
    {
        var builder = ValidBuilder(new Quaternion(0, 0, 0, 2), default);

        Assert.Throws<InvalidDataException>(() => builder.Build());
    }

    [Fact]
    public void Builder_PlayerRotationNotNormalized_Throws()
    {
        var builder = ValidBuilder(Quaternion.Identity, default);
        builder.AddPlayer(new PlayerRecord(
            new PlayerId(1), new PlatformIdentity(IdentityPlatform.Dev, 1), "x", new EntityId(2), FrameRef.Of(SimFixture.Planet), 0, null,
            new Vector3(0, 4000, 0), new Quaternion(0, 0, 0, 0), 0, false));

        Assert.Throws<InvalidDataException>(() => builder.Build());
    }

    /// <summary>World with construct 1 (one block in <paramref name="chunk"/>) and room for entity 2 and player 1.</summary>
    private static WorldStateBuilder ValidBuilder(Quaternion constructRotation, ChunkPos chunk)
    {
        var template = Testing.WorldFactory.CreateDefault();
        var builder = new WorldStateBuilder(new WorldId(Guid.NewGuid()), 1);
        builder.SetProgress(OpId.None, default, 3, 2);
        builder.AddPlanet(Testing.WorldFactory.HomePlanetParams);
        foreach (var entry in template.BlockTypes.Entries)
        {
            builder.AddBlockType(entry);
        }

        builder.AddConstruct(new ConstructHeaderRecord(
            new EntityId(1), FrameRef.Of(SimFixture.Planet), 0, ConstructMode.Static, new Vector3(0, 4000, 0), constructRotation, 1));
        var blocks = new uint[ConstructChunk.CellCount];
        blocks[0] = BlockState.Create(1, 0).Packed;
        builder.AddChunk(new ConstructChunkRecord(new ConstructChunkKey(new EntityId(1), chunk), blocks));
        return builder;
    }

    internal static WorldState Load(WorldState live, IReadOnlyList<WorldCheckpoint> checkpoints)
    {
        var constructs = new SortedDictionary<ulong, ConstructHeaderRecord>();
        var chunks = new Dictionary<ConstructChunkKey, ConstructChunkRecord>();
        var players = new SortedDictionary<ulong, PlayerRecord>();
        var terrain = new SortedDictionary<TerrainBlockKey, TerrainBlockVersion>();
        foreach (var checkpoint in checkpoints)
        {
            foreach (var header in checkpoint.UpsertedConstructs)
            {
                constructs[header.Id.Value] = header;
            }

            foreach (var id in checkpoint.RemovedConstructs)
            {
                constructs.Remove(id.Value);
            }

            foreach (var chunk in checkpoint.UpsertedChunks)
            {
                chunks[chunk.Key] = chunk;
            }

            foreach (var key in checkpoint.RemovedChunks)
            {
                chunks.Remove(key);
            }

            foreach (var player in checkpoint.UpsertedPlayers)
            {
                players[player.Player.Value] = player;
            }

            foreach (var version in checkpoint.TerrainVersions)
            {
                terrain[version.Key] = version;
            }
        }

        var last = checkpoints[^1];
        var builder = new WorldStateBuilder(live.Id, live.GalaxySeed);
        builder.SetProgress(last.BarrierOpId, last.Tick, last.NextEntityId, last.NextPlayerId);
        foreach (var planet in live.Planets.All)
        {
            builder.AddPlanet(planet);
        }

        foreach (var entry in live.BlockTypes.Entries)
        {
            builder.AddBlockType(entry);
        }

        foreach (var entry in live.Materials.Entries)
        {
            builder.AddMaterial(entry);
        }

        foreach (var header in constructs.Values)
        {
            builder.AddConstruct(header);
        }

        foreach (var chunk in chunks.Values)
        {
            builder.AddChunk(chunk);
        }

        foreach (var player in players.Values)
        {
            builder.AddPlayer(player);
        }

        foreach (var version in terrain.Values)
        {
            builder.AddTerrainVersion(version);
        }

        return builder.Build();
    }
}
