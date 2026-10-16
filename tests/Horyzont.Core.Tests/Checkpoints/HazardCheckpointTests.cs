using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Survival;
using Horyzont.Core.World;

using static Horyzont.Core.Tests.Survival.HazardWorld;

namespace Horyzont.Core.Tests.Checkpoints;

/// <summary>K6-34: the suit of the players and the oxygen generators travel through the checkpoint, the builder and the journal tail (D4, D6, D12).</summary>
public sealed class HazardCheckpointTests
{
    private static readonly SuitResources _suit = new(100f, 50f, 0f, 25f, 75.5f);

    [Fact]
    public void Capture_OnlinePlayer_CarriesSuit()
    {
        var f = new HazardWorld(players: 2);
        f.SetSuit(Player1, _suit);
        Complete(f);

        // The second capture changes nothing in the journal, yet every online player is in it, with the suit set meanwhile and without a change mark.
        var later = _suit with { Oxygen = 60f };
        f.SetSuit(Player1, later);
        var checkpoint = f.State.CaptureCheckpoint();

        var one = Assert.Single(checkpoint.UpsertedPlayers, p => p.Player == Player1);
        Assert.Equal(later, one.Vitals!.Value.Suit);
        var two = Assert.Single(checkpoint.UpsertedPlayers, p => p.Player == Player2);
        Assert.Null(two.Vitals);
    }

    [Fact]
    public void Builder_Suit_RestoredIntoPlayerState()
    {
        var f = new HazardWorld(players: 2);
        f.SetSuit(Player1, _suit);
        f.SetSuit(Player2, null);
        var checkpoint = f.State.CaptureCheckpoint();

        var loaded = Load(f.State, checkpoint);

        Assert.True(loaded.Players.TryGet(Player1, out var one));
        Assert.Equal(_suit, one.Vitals!.Value.Suit);
        Assert.True(loaded.Players.TryGet(Player2, out var two));
        Assert.NotNull(two.Vitals);
        Assert.Null(two.Vitals!.Value.Suit);

        // The suit is outside the state hash: the loaded world and the live world agree.
        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
    }

    [Fact]
    public void Builder_InvalidSuit_Throws()
    {
        var f = new HazardWorld();
        f.SetSuit(Player1, _suit);
        var checkpoint = f.State.CaptureCheckpoint();
        Load(f.State, checkpoint);

        foreach (var bad in new[] { float.NaN, -1f, 1001f, float.PositiveInfinity })
        {
            var players = checkpoint.UpsertedPlayers.Select(p => p with { Vitals = new PlayerVitals(10f, 10f, default, false, _suit with { Shield = bad }) });
            Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedPlayers = [.. players] }));
        }
    }

    [Fact]
    public void Builder_GeneratorRestored_WithIndex()
    {
        var f = new HazardWorld();
        var input = f.SetUpGenerator(GeneratorCell, new Tick(100));
        f.FeedGenerator(input, 0, 2);
        var checkpoint = f.State.CaptureCheckpoint();

        var loaded = Load(f.State, checkpoint);

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
        Assert.True(loaded.BlockEntities.TryGet(new BlockRef(FunctionalBlockFixture.ConstructId, GeneratorCell), out var state));
        Assert.Equal(new OxygenGeneratorBlockState(input, new Tick(100)), state);
        Assert.Equal([new BlockRef(FunctionalBlockFixture.ConstructId, GeneratorCell)], loaded.BlockEntities.Generators);
    }

    [Fact]
    public void Builder_GeneratorWithoutInput_Throws()
    {
        var f = new HazardWorld();
        var input = f.SetUpGenerator(GeneratorCell);
        var checkpoint = f.State.CaptureCheckpoint();
        Load(f.State, checkpoint);

        // A generator state whose input container does not exist, is not a StationInput of its block or has an output next to it is a corrupt world.
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedContainers = [.. checkpoint.UpsertedContainers.Where(c => c.Id != input)] }));
        var wrongKind = checkpoint.UpsertedContainers.Select(c => c.Id == input ? c with { Kind = ContainerKind.Chest } : c);
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedContainers = [.. wrongKind] }));
    }

    [Fact]
    public void Builder_GeneratorWithOutputNextToIt_Throws()
    {
        var f = new HazardWorld();
        var input = f.SetUpGenerator(GeneratorCell);
        var checkpoint = f.State.CaptureCheckpoint();
        Load(f.State, checkpoint);
        var original = Assert.Single(checkpoint.UpsertedContainers, c => c.Id == input);
        foreach (var kind in new[] { ContainerKind.StationInput, ContainerKind.StationOutput })
        {
            var extra = original with { Id = new ContainerId(checkpoint.NextContainerId), Kind = kind };
            var corrupt = checkpoint with
            {
                NextContainerId = checkpoint.NextContainerId + 1,
                UpsertedContainers = [.. checkpoint.UpsertedContainers, extra],
            };
            Assert.Throws<InvalidDataException>(() => Load(f.State, corrupt));
        }
    }

    [Fact]
    public void Builder_InputWithoutGenerator_Throws()
    {
        var f = new HazardWorld();
        f.SetUpGenerator(GeneratorCell);
        var checkpoint = f.State.CaptureCheckpoint();
        Load(f.State, checkpoint);

        // The container of a generator without the state of the generator (and the other way round, above) is a corrupt world.
        Assert.Throws<InvalidDataException>(() => Load(f.State, checkpoint with { UpsertedBlockEntities = [] }));
    }

    [Fact]
    public void CheckpointPlusReplay_RefillAndSettlement_SameHash()
    {
        var f = new HazardWorld(players: 2);
        var input = f.SetUpGenerator(GeneratorCell);
        f.FeedGenerator(input, 0, 3);
        f.SetSuit(Player1, _suit with { Oxygen = 30f });
        var checkpoint = Complete(f);

        f.Give(Player1, Canister, 2);
        f.Apply(new CompoundOp([new ItemDestroyOp(BagSlot(Player1, 0), Canister, 1, ItemSink.SuitRefill), Refill(Player1, SuitResource.Oxygen, 50, 100)]));
        f.Apply(GeneratorState(GeneratorCell, input, f.Now));
        f.AdvanceTicks(100);

        // The settlement of the generator (D12): the burned cell is destroyed with the sink Fuel and the start moves, in one operation.
        f.Apply(new CompoundOp([new ItemDestroyOp(new SlotRef(input, 0), Fuel, 1, ItemSink.Fuel), GeneratorState(GeneratorCell, input, f.Now)]));
        f.Apply(Refill(Player1, SuitResource.Filter, 10, 100));
        var tail = f.Blocks.Sim.Journal.Entries.Where(e => e.Id > checkpoint.BarrierOpId).ToList();
        Assert.True(tail.Count >= 5, $"{tail.Count} operations in the tail");

        var loaded = Load(f.State, checkpoint);
        Assert.Equal(30f, loaded.Players.TryGet(Player1, out var restored) ? restored.Vitals!.Value.Suit!.Value.Oxygen : -1f);
        var replay = new Horyzont.Core.Tests.SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(replay.State));
        Assert.Equal(2, replay.State.Containers.TryGet(input, out var fuel) ? fuel.GetSlot(0).Count : -1);

        // The checkpoint had the oxygen at 30; the refill from the tail brought it to 80 again, and the filter refill from the tail applied too.
        Assert.True(replay.State.Players.TryGet(Player1, out var player));
        Assert.Equal(80f, player.Vitals!.Value.Suit!.Value.Oxygen);
        Assert.Equal(10f, player.Vitals.Value.Suit.Value.Filter);
        Assert.Equal(f.SuitOf(Player1), player.Vitals.Value.Suit);
    }

    [Fact]
    public void Replay_RefillOfOfflinePlayer_MarkedForCheckpoint()
    {
        var f = new HazardWorld(players: 2);
        f.SetSuit(Player1, _suit);
        var state = f.Blocks.Sim.PlayerOf(Player1);
        f.Apply(new PlayerLeaveOp(Player1, state.Frame, state.ParentSeq, state.Position, Quaternion.Identity, 0f));
        var checkpoint = Complete(f);
        f.Apply(Refill(Player1, SuitResource.Oxygen, 10, 100));
        var tail = f.Blocks.Sim.Journal.Entries.Where(e => e.Id > checkpoint.BarrierOpId).ToList();

        // The world after a crash: the checkpoint and the tail. The refill of an offline player must put the record into the next checkpoint, with the new suit.
        var loaded = Load(f.State, checkpoint);
        var first = loaded.CaptureCheckpoint();
        Assert.DoesNotContain(first.UpsertedPlayers, p => p.Player == Player1);
        loaded.CompleteCheckpoint(first);
        var replay = new Horyzont.Core.Tests.SimFixture(state: loaded);
        foreach (var entry in tail)
        {
            replay.Sim.Replay(entry);
        }

        var next = replay.State.CaptureCheckpoint();
        var record = Assert.Single(next.UpsertedPlayers, p => p.Player == Player1);
        Assert.False(record.IsOnline);
        Assert.Equal(85.5f, record.Vitals!.Value.Suit!.Value.Oxygen);
    }

    private static WorldCheckpoint Complete(HazardWorld f)
    {
        var checkpoint = f.State.CaptureCheckpoint();
        f.State.CompleteCheckpoint(checkpoint);
        return checkpoint;
    }

    /// <summary>A world from one full checkpoint (the first capture of a world contains every construct, container, player and state).</summary>
    private static WorldState Load(WorldState live, WorldCheckpoint checkpoint)
    {
        var builder = new WorldStateBuilder(live.Id, live.GalaxySeed);
        builder.SetProgress(checkpoint.BarrierOpId, checkpoint.Tick, checkpoint.NextEntityId, checkpoint.NextPlayerId);
        builder.SetNextContainerId(checkpoint.NextContainerId);
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

        foreach (var entry in live.ItemTypes.Entries)
        {
            builder.AddItemType(entry);
        }

        foreach (var header in checkpoint.UpsertedConstructs)
        {
            builder.AddConstruct(header);
        }

        foreach (var chunk in checkpoint.UpsertedChunks)
        {
            builder.AddChunk(chunk);
        }

        foreach (var player in checkpoint.UpsertedPlayers)
        {
            builder.AddPlayer(player);
        }

        foreach (var container in checkpoint.UpsertedContainers)
        {
            builder.AddContainer(container);
        }

        foreach (var entity in checkpoint.UpsertedBlockEntities)
        {
            builder.AddBlockEntity(entity);
        }

        foreach (var progress in checkpoint.UpsertedProgress)
        {
            builder.AddPlayerProgress(progress);
        }

        if (checkpoint.ProgressKeys is { } keys)
        {
            builder.SetProgressKeys(keys);
        }

        foreach (var version in checkpoint.TerrainVersions)
        {
            builder.AddTerrainVersion(version);
        }

        return builder.Build();
    }
}
