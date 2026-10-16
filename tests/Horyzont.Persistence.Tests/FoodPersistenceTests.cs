using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.World;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>
/// K6-33 stage C: the food slots (blob 22 version 3) and the vitals of a player (blob 2 version 2) go through the checkpoint and come back, and the worlds
/// saved by the earlier packages still load. No migration: the tables are those of M0001 and M0003 (D3).
/// </summary>
public sealed class FoodPersistenceTests
{
    public const string FixtureName = "world-v3-food.db";

    // The hash of the world of the fixture after the replay of its tail, taken when the fixture was generated (K6-33 D19).
    private const ulong ExpectedFoodHash = 5250775705113734629UL;

    private static LoadedWorld LoadOnly(string path) =>
        WorldLoader.Load(path, WorldHarness.LoadOptions(path), new FakeTimeProvider(), NullLogger.Instance);

    private static string CopyFixture(TempDirectory dir, string name)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Assert.True(File.Exists(fixture), $"Missing {fixture}; generate it once with FixtureGenerator (Category=Manual) and commit it.");
        File.Copy(fixture, dir.WorldPath);
        return dir.WorldPath;
    }

    [Fact]
    public async Task Checkpoint_PlayerVitals_RoundTripThroughSqlite()
    {
        using var dir = new TempDirectory();
        FoodWorldBuilt built;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FoodWorldScript.NewWorld()))
        {
            built = FoodWorldScript.Build(h);

            // The online player is always written; Bob left, which marks him changed, so both rows carry their vitals.
            Assert.True(await h.CheckpointAndPump());

            // The vitals are not journaled: a change after the checkpoint is lost with the crash, the checkpoint value comes back.
            Assert.True(h.Sim.UpdatePlayerVitals(built.Alice, new PlayerVitals(10f, 10f, new Tick(0), false)));
            await h.FlushAndPump();
        }

        var loaded = LoadOnly(dir.WorldPath);
        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(loaded.State.Players.TryGet(built.Alice, out var alice));
        Assert.Equal(FoodWorldScript.AliceVitals, alice.Vitals);
        Assert.True(loaded.State.Players.TryGet(built.Bob, out var bob));
        Assert.False(bob.IsOnline);
        Assert.Equal(FoodWorldScript.BobVitals, bob.Vitals);
        Assert.True(sim.State.Players.TryGet(built.Alice, out var replayed));
        Assert.Equal(FoodWorldScript.AliceVitals, replayed.Vitals);
    }

    [Fact]
    public async Task Checkpoint_FoodProgress_RoundTrip()
    {
        using var dir = new TempDirectory();
        FoodWorldBuilt built;
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: FoodWorldScript.NewWorld()))
        {
            built = FoodWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());

            // Both players have a row: Alice with the bed, the discoveries and the food, Bob with the food only.
            Assert.Equal(2L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM player_progress;"));
            FoodWorldScript.BuildTail(h, built);
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
        }

        var loaded = LoadOnly(dir.WorldPath);
        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(loaded.State.Progress.TryGet(built.Alice, out var alice));
        Assert.NotNull(alice.RespawnBed);
        Assert.Equal(2, alice.DiscoveredItems.Count);
        Assert.Equal(new FoodSlot(FoodWorldScript.Berries, new Tick(100)), alice.Food[0]);
        Assert.Equal(new FoodSlot(FoodWorldScript.Jam, new Tick(200)), alice.Food[1]);
        Assert.True(alice.Food[2].IsEmpty);
        Assert.True(loaded.State.Progress.TryGet(built.Bob, out var bob));
        Assert.Null(bob.RespawnBed);
        Assert.Equal(0, bob.DiscoveredItems.Count);
        Assert.Equal(new FoodSlot(FoodWorldScript.Berries, new Tick(50)), bob.Food[0]);

        // The tail (the broth eaten into the third slot) is replayed on top of the checkpoint and gives the hash of the world before the restart.
        Assert.Equal(2, loaded.JournalTail.Count);
        Assert.True(sim.State.Progress.TryGet(built.Alice, out var replayed));
        Assert.Equal(new FoodSlot(FoodWorldScript.Broth, new Tick(300)), replayed.Food[2]);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public void Load_WorldV3Blocks_PlayersHaveNullVitals()
    {
        using var dir = new TempDirectory();
        var path = CopyFixture(dir, MigrationTests.FixtureV3BlocksName);

        // Version 1 of blob 2: the saved players have no vitals (a fresh character gets the full maxima at its first tick).
        var loaded = LoadOnly(path);

        Assert.NotEmpty(loaded.State.Players.All);
        Assert.All(loaded.State.Players.All, player => Assert.Null(player.Vitals));
    }

    [Fact]
    public void Load_WorldV3Crafting_ReadsProgressV2()
    {
        using var dir = new TempDirectory();
        var path = CopyFixture(dir, MigrationTests.FixtureV3CraftingName);

        // The K6-32 fixture holds blob 22 in version 2 (bed and discoveries): it reads, and the food is empty.
        var loaded = LoadOnly(path);

        Assert.True(loaded.State.Progress.TryGet(new PlayerId(1), out var alice));
        Assert.NotNull(alice.RespawnBed);
        Assert.Equal(2, alice.DiscoveredItems.Count);
        Assert.True(alice.Food.IsEmpty);
        Assert.True(loaded.State.Progress.TryGet(new PlayerId(2), out var bob));
        Assert.True(bob.Food.IsEmpty);
    }

    [Fact]
    public void Load_WorldV3Crafting_NewFoodKeysRegistered()
    {
        using var dir = new TempDirectory();
        var path = CopyFixture(dir, MigrationTests.FixtureV3CraftingName);
        var keys = RepoContent.Load().Items.Items.Select(i => i.Key).ToArray();
        string[] foods = ["core:berries", "core:berry_jam", "core:herbal_broth"];
        Assert.All(foods, key => Assert.Contains(key, keys));

        // A world saved before the food gets the item types of the food when it is loaded with the content of the game (the loader grows the registry), so the
        // bush never finds an item missing from the world (core review of K6-33, B6 weak point f).
        var loaded = WorldLoader.Load(
            path, WorldHarness.LoadOptions(path) with { ItemTypeKeys = keys }, new FakeTimeProvider(), NullLogger.Instance);

        foreach (var key in foods)
        {
            Assert.True(loaded.State.ItemTypes.TryGetId(key, out _), $"{key} is not registered.");
        }

        // What the fixture already had keeps its numbers, and its players still load.
        Assert.Equal(SurvivalContentFactory.CraftingItemKeys, loaded.State.ItemTypes.Entries.Take(SurvivalContentFactory.CraftingItemKeys.Count).Select(e => e.Key));
        Assert.True(loaded.State.Progress.TryGet(new PlayerId(1), out _));
    }

    [Fact]
    public void FixtureWorldV3Food_CopiedToTemp_LoadsAndReplays()
    {
        using var dir = new TempDirectory();
        var path = CopyFixture(dir, FixtureName);
        Assert.Equal(3L, RawDb.Long(path, "PRAGMA user_version;"));

        var checkpointed = LoadOnly(path);
        var (loaded, sim, _) = WorldHarness.LoadAndReplay(path);

        // The checkpoint holds Alice (online, bed, discoveries, two meals) and Bob (offline, one meal, no bed, no discoveries), both with their vitals.
        // The tail is the broth and its eating (K6-33 D19). The schema stays at version 3: no migration.
        Assert.Equal(2, loaded.JournalTail.Count);
        Assert.True(checkpointed.State.Players.TryGet(new PlayerId(1), out var alice));
        Assert.True(alice.IsOnline);
        Assert.Equal(FoodWorldScript.AliceVitals, alice.Vitals);
        Assert.True(checkpointed.State.Players.TryGet(new PlayerId(2), out var bob));
        Assert.False(bob.IsOnline);
        Assert.Equal(FoodWorldScript.BobVitals, bob.Vitals);
        Assert.True(sim.State.Progress.TryGet(new PlayerId(1), out var aliceProgress));
        Assert.NotNull(aliceProgress.RespawnBed);
        Assert.False(aliceProgress.Food[0].IsEmpty || aliceProgress.Food[1].IsEmpty || aliceProgress.Food[2].IsEmpty);
        Assert.True(sim.State.Progress.TryGet(new PlayerId(2), out var bobProgress));
        Assert.Null(bobProgress.RespawnBed);
        Assert.False(bobProgress.Food[0].IsEmpty);
        Assert.True(sim.State.Players.TryGet(new PlayerId(1), out var replayed));
        Assert.Equal(FoodWorldScript.AliceVitals, replayed.Vitals);
        Assert.Equal(ExpectedFoodHash, StateHasher.Hash(sim.State));
    }
}
