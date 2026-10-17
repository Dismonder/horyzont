using Horyzont.Core.BlockEntities;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Survival;
using Horyzont.Persistence.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>
/// K6-34 stage C: the suit of a player (blob 2 version 3) and the state of the oxygen generators (kind 5 of blob 21) go through the checkpoint and come back, the journal tail (a refill and the settlement of a generator)
/// is replayed on top, and the worlds saved by the earlier packages still load with the suit unknown. No migration: the tables are those of M0001 and M0003 (D3).
/// </summary>
public sealed class HazardPersistenceTests
{
    public const string FixtureName = "world-v3-hazards.db";

    // The hash of the world of the fixture after the replay of its tail, taken when the fixture was generated (K6-34 D21).
    private const ulong ExpectedHazardHash = 2385176141588066832UL;

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
    public async Task Checkpoint_SuitRoundTripThroughSqlite()
    {
        using var dir = new TempDirectory();
        HazardWorldBuilt built;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: HazardWorldScript.NewWorld()))
        {
            built = HazardWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());

            // The suit is not journaled, like the health: a change after the checkpoint is lost with the crash and the checkpoint value comes back.
            Assert.True(h.Sim.UpdatePlayerVitals(built.Alice, HazardWorldScript.AliceVitals with { Suit = new SuitResources(1f, 1f, 1f, 1f, 1f) }));
            await h.FlushAndPump();
        }

        var loaded = LoadOnly(dir.WorldPath);

        Assert.True(loaded.State.Players.TryGet(built.Alice, out var alice));
        Assert.Equal(HazardWorldScript.AliceVitals, alice.Vitals);
        Assert.Equal(HazardWorldScript.AliceSuit, alice.Vitals!.Value.Suit);
        Assert.True(loaded.State.Players.TryGet(built.Bob, out var bob));
        Assert.False(bob.IsOnline);
        Assert.Equal(HazardWorldScript.BobVitals, bob.Vitals);
        Assert.Null(bob.Vitals!.Value.Suit);
    }

    [Fact]
    public async Task Checkpoint_GeneratorState_RoundTrip()
    {
        using var dir = new TempDirectory();
        HazardWorldBuilt built;
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: HazardWorldScript.NewWorld()))
        {
            built = HazardWorldScript.Build(h);
            Assert.True(await h.CheckpointAndPump());
            hash = StateHasher.Hash(h.State);
        }

        var loaded = LoadOnly(dir.WorldPath);

        // The burning generator keeps its start and its two fuel packs, the idle one has no start and an empty input, and the world hashes as before the restart.
        Assert.True(loaded.State.BlockEntities.TryGet(new BlockRef(built.Construct, HazardWorldScript.BurningCell), out var burning));
        Assert.Equal(new OxygenGeneratorBlockState(built.BurningInput, HazardWorldScript.BurningStart), burning);
        Assert.True(loaded.State.BlockEntities.TryGet(new BlockRef(built.Construct, HazardWorldScript.IdleCell), out var idle));
        Assert.Equal(new OxygenGeneratorBlockState(built.IdleInput, null), idle);
        Assert.Equal(2, loaded.State.BlockEntities.Generators.Count);
        Assert.True(loaded.State.Containers.TryGet(built.BurningInput, out var input));
        Assert.Equal(new ItemStack(HazardWorldScript.Fuel, 2), input.GetSlot(0));
        Assert.Equal(hash, StateHasher.Hash(loaded.State));
    }

    [Fact]
    public void Load_WorldV3Food_SuitNull()
    {
        using var dir = new TempDirectory();
        var path = CopyFixture(dir, FoodPersistenceTests.FixtureName);

        // The K6-33 fixture holds blob 2 in version 2: the vitals read, and the suit is unknown (a fresh suit is filled by the first tick with the hazard content).
        var loaded = LoadOnly(path);

        Assert.True(loaded.State.Players.TryGet(new PlayerId(1), out var alice));
        Assert.Equal(FoodWorldScript.AliceVitals, alice.Vitals);
        Assert.Null(alice.Vitals!.Value.Suit);
        Assert.True(loaded.State.Players.TryGet(new PlayerId(2), out var bob));
        Assert.Null(bob.Vitals!.Value.Suit);
    }

    [Fact]
    public void FixtureWorldV3Hazards_CopiedToTemp_LoadsAndReplays()
    {
        using var dir = new TempDirectory();
        var path = CopyFixture(dir, FixtureName);
        Assert.Equal(3L, RawDb.Long(path, "PRAGMA user_version;"));

        var checkpointed = LoadOnly(path);
        var (loaded, sim, _) = WorldHarness.LoadAndReplay(path);

        // The checkpoint holds Alice (online, a tank in the module slot, the suit) and Bob (offline, no suit), two generators; the tail is one refill and one settlement.
        Assert.Equal(2, loaded.JournalTail.Count);
        Assert.True(checkpointed.State.Players.TryGet(new PlayerId(1), out var alice));
        Assert.Equal(HazardWorldScript.AliceVitals, alice.Vitals);
        Assert.True(checkpointed.State.Players.TryGet(new PlayerId(2), out var bob));
        Assert.Equal(HazardWorldScript.BobVitals, bob.Vitals);

        // The refill of the tail raised the oxygen by 50 (75.5 to 125.5, under the capacity 200 of the operation) and nothing else; the settlement moved the start and burnt one fuel pack.
        Assert.True(sim.State.Players.TryGet(new PlayerId(1), out var replayed));
        Assert.Equal(HazardWorldScript.AliceSuit with { Oxygen = 125.5f }, replayed.Vitals!.Value.Suit);
        Assert.True(sim.State.BlockEntities.TryGet(new BlockRef(sim.State.Constructs.All[0].Id, HazardWorldScript.BurningCell), out var burning));
        var generator = Assert.IsType<OxygenGeneratorBlockState>(burning);
        Assert.Equal(HazardWorldScript.SettledStart, generator.Started);
        Assert.True(sim.State.Containers.TryGet(generator.Input, out var input));
        Assert.Equal(new ItemStack(HazardWorldScript.Fuel, 1), input.GetSlot(0));
        Assert.Equal(ExpectedHazardHash, StateHasher.Hash(sim.State));
    }
}
