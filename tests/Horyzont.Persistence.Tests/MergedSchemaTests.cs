using Horyzont.Core.Hashing;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class MergedSchemaTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Load_OlderSpaceSchema_PreservesInventoryAfterCheckpointAndReplay(int version)
    {
        using var dir = new TempDirectory();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world-v1.db"), dir.WorldPath);
        if (version == 2)
        {
            using var connection = RawDb.Open(dir.WorldPath);
            Persistence.Migrations.MigrationRunner.Run(connection, Persistence.Migrations.MigrationRunner.All.Take(2).ToArray());
        }

        ulong hash;
        await using (var h = WorldHarness.Open(dir.WorldPath,
            loadOptions: WorldHarness.LoadOptions(dir.WorldPath) with { ItemTypeKeys = SurvivalContentFactory.ItemKeys }))
        {
            Assert.Equal(3L, RawDb.Long(dir.WorldPath, "PRAGMA user_version;"));
            Assert.NotNull(h.State.StarSystem);
            Assert.Equal(4, h.State.Planets.All.Count);
            var player = h.Join(50);
            h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(player, 1));
            h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.HotbarOf(1), 0),
                WorldFactory.Item(SurvivalContentFactory.ToolKey), 1, 1, ItemSource.Starter));
            Assert.True(await h.CheckpointAndPump());
            h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.BagOf(1), 5),
                WorldFactory.Item(SurvivalContentFactory.StoneKey), 40, 500, ItemSource.Harvest));
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Single(loaded.JournalTail);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
        Assert.Equal(3, sim.State.Containers.Count);
        Assert.Equal(4UL, sim.State.ContainerIds.NextValue);
        Assert.Equal(SurvivalContentFactory.ItemKeys, sim.State.ItemTypes.Entries.Select(entry => entry.Key));
        Assert.True(sim.State.Containers.TryGet(WorldFactory.HotbarOf(1), out var hotbar));
        Assert.Equal(new ItemStack(WorldFactory.Item(SurvivalContentFactory.ToolKey), 1), hotbar.GetSlot(0));
        Assert.True(sim.State.Containers.TryGet(WorldFactory.BagOf(1), out var bag));
        Assert.Equal(new ItemStack(WorldFactory.Item(SurvivalContentFactory.StoneKey), 40), bag.GetSlot(5));
        var backup = Assert.Single(Directory.GetFiles(dir.BackupsPath));
        Assert.Equal((long)version, RawDb.Long(backup, "PRAGMA user_version;"));
    }

    [Fact]
    public void Load_PreMergeSurvivalV2_RejectsExplicitlyWithoutChangingData()
    {
        using var dir = new TempDirectory();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world-v3.db"), dir.WorldPath);
        // Reconstruct the old branch: Survival occupied migration 2 and Space was absent.
        RawDb.Execute(dir.WorldPath,
            "DROP TABLE star_systems; DROP TABLE markers; DROP TABLE ship_state; PRAGMA user_version = 2;");
        var journal = RawDb.JournalIds(dir.WorldPath);
        var containers = RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM containers;");

        var error = Assert.Throws<WorldCorruptedException>(() => WorldLoader.Load(dir.WorldPath,
            WorldHarness.LoadOptions(dir.WorldPath), new FakeTimeProvider(), NullLogger.Instance));

        Assert.Contains("pre-merge Survival", error.Message, StringComparison.Ordinal);
        Assert.Contains("schema 2", error.Message, StringComparison.Ordinal);
        Assert.Equal(2L, RawDb.Long(dir.WorldPath, "PRAGMA user_version;"));
        Assert.Equal(containers, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM containers;"));
        Assert.Equal(journal, RawDb.JournalIds(dir.WorldPath));
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'star_systems';"));
    }
}
