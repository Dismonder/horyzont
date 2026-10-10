using Horyzont.Core.Hashing;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class ContainerPersistenceTests
{
    private static WorldState SurvivalWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey], ["stone", "dirt"],
        SurvivalContentFactory.ItemKeys);

    private static LoadedWorld Load(string path, IReadOnlyList<string>? itemTypes = null) =>
        WorldLoader.Load(
            path, WorldHarness.LoadOptions(path) with { ItemTypeKeys = itemTypes ?? [] }, new FakeTimeProvider(), NullLogger.Instance);

    private static void CreateContainersAndItems(WorldHarness h)
    {
        var player = h.Join(1);
        h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(player, 1));
        h.Sim.ApplyServerOperation(new ItemCreateOp(
            new SlotRef(WorldFactory.HotbarOf(1), 0), WorldFactory.Item(SurvivalContentFactory.ToolKey), 1, 1, ItemSource.Starter));
    }

    [Fact]
    public async Task Checkpoint_WritesContainersAndNextId_LoadRestores()
    {
        using var dir = new TempDirectory();
        ulong hash;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: SurvivalWorld()))
        {
            CreateContainersAndItems(h);
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(3L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM containers;"));
            Assert.Equal(4L, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'next_container_id';"));

            h.Sim.ApplyServerOperation(new ItemCreateOp(
                new SlotRef(WorldFactory.BagOf(1), 5), WorldFactory.Item(SurvivalContentFactory.StoneKey), 40, 500, ItemSource.Harvest));
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
        }

        var (loaded, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(3, loaded.State.Containers.Count);
        Assert.Equal(4UL, loaded.State.ContainerIds.NextValue);
        Assert.Single(loaded.JournalTail);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
        Assert.True(sim.State.Containers.TryGet(WorldFactory.BagOf(1), out var bag));
        Assert.Equal(new ItemStack(WorldFactory.Item(SurvivalContentFactory.StoneKey), 40), bag.GetSlot(5));
    }

    [Fact]
    public async Task Checkpoint_ChangedContainerAgain_UpsertsTheRow()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: SurvivalWorld()))
        {
            CreateContainersAndItems(h);
            Assert.True(await h.CheckpointAndPump());
            h.Sim.ApplyServerOperation(new ItemDestroyOp(
                new SlotRef(WorldFactory.HotbarOf(1), 0), WorldFactory.Item(SurvivalContentFactory.ToolKey), 1, ItemSink.Admin));
            Assert.True(await h.CheckpointAndPump());
        }

        Assert.Empty(RawDb.JournalIds(dir.WorldPath));
        var loaded = Load(dir.WorldPath);

        Assert.True(loaded.State.Containers.TryGet(WorldFactory.HotbarOf(1), out var hotbar));
        Assert.True(hotbar.GetSlot(0).IsEmpty);
        Assert.Equal(3U, hotbar.Version);
    }

    [Fact]
    public void ItemTypes_GrowOnLoad_KeepIds()
    {
        using var dir = new TempDirectory();
        var world = WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [], [WorldFactory.StoneBlockKey], ["stone"], ["core:a", "core:b"]);
        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, new FakeTimeProvider());

        var first = Load(dir.WorldPath, ["core:b", "core:c", "core:a", "core:d"]);

        Assert.True(first.State.ItemTypes.TryGetId("core:a", out var a));
        Assert.True(first.State.ItemTypes.TryGetId("core:b", out var b));
        Assert.True(first.State.ItemTypes.TryGetId("core:c", out var c));
        Assert.True(first.State.ItemTypes.TryGetId("core:d", out var d));
        Assert.Equal([1, 2, 3, 4], new[] { a.Value, b.Value, c.Value, d.Value });

        var second = Load(dir.WorldPath);
        Assert.Equal(first.State.ItemTypes.Entries, second.State.ItemTypes.Entries);
    }

    [Fact]
    public async Task CorruptContainerBlob_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: SurvivalWorld()))
        {
            CreateContainersAndItems(h);
            Assert.True(await h.CheckpointAndPump());
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE containers SET data = $data WHERE container_id = 2;", ("$data", new byte[] { 20, 1, 0xFF, 0xFF }));

        Assert.Throws<WorldCorruptedException>(() => Load(dir.WorldPath));
    }

    [Fact]
    public async Task ContainerRowForAnotherId_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, world: SurvivalWorld()))
        {
            CreateContainersAndItems(h);
            Assert.True(await h.CheckpointAndPump());
        }

        dir.ReleasePools();
        RawDb.Execute(dir.WorldPath, "UPDATE containers SET container_id = 99 WHERE container_id = 2;");

        Assert.Throws<WorldCorruptedException>(() => Load(dir.WorldPath));
    }
}
