using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32 (spec 03, tests): crafting from five chests is one atomic compound (D12). Stage B builds the compound from an intent; here the operations are built by hand.</summary>
public sealed class CraftCompoundTests
{
    private static readonly CellPos[] _chestCells = [FunctionalBlocks.ChestCell, FunctionalBlocks.FreeCell, FunctionalBlocks.FreeCell2, new(7, 1, 0), new(8, 1, 0)];

    [Fact]
    public void CraftFromFiveChests_OneCompound_AppliedWhole()
    {
        var f = new CraftingFixture();
        var chests = FiveChestsWithStone(f);
        var journaled = f.Blocks.Sim.Journal.Entries.Count;

        var applied = f.Apply(Craft(chests, failLast: false));

        Assert.Equal(journaled + 1, f.Blocks.Sim.Journal.Entries.Count);
        foreach (var chest in chests)
        {
            Assert.True(f.Slot(chest, 0).IsEmpty);
        }

        Assert.Equal(new ItemStack(CraftingFixture.Gem, 5), f.Slot(Bag, 0));
        Assert.Equal(6, applied.ContainerChanges.Count);
        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.True(progress.DiscoveredItems.Contains(CraftingFixture.Gem));
    }

    [Fact]
    public void CraftFromFiveChests_LastComponentFails_NothingChanged()
    {
        var accepted = new CraftingFixture();
        accepted.Apply(Craft(FiveChestsWithStone(accepted), failLast: false));

        var f = new CraftingFixture();
        var chests = FiveChestsWithStone(f);
        var nextContainer = f.State.ContainerIds.NextValue;
        var versions = f.State.Containers.All.Select(c => c.Version).ToArray();

        // The compound is the accepted one plus a destroy of an item the bag does not hold, at the end (hash, last operation and journal are checked).
        f.AssertFails(Craft(chests, failLast: true));

        Assert.Equal(nextContainer, f.State.ContainerIds.NextValue);
        Assert.Equal(versions, f.State.Containers.All.Select(c => c.Version));
        foreach (var chest in chests)
        {
            Assert.Equal(new ItemStack(CraftingFixture.Stone, 20), f.Slot(chest, 0));
        }

        Assert.True(f.Slot(Bag, 0).IsEmpty);
        Assert.Equal(0, f.State.Progress.Count);
    }

    private static ContainerId[] FiveChestsWithStone(CraftingFixture f)
    {
        var chests = new ContainerId[_chestCells.Length];
        for (var i = 0; i < chests.Length; i++)
        {
            chests[i] = f.Blocks.SetUpChestAt(_chestCells[i], Player1, ContainerAccess.Shared, 4);
            f.Blocks.Give(chests[i], 0, 20);
        }

        return chests;
    }

    /// <summary>100 stone from five chests (20 each) become 5 gems in the bag; the gem is discovered in the same compound.</summary>
    private static CompoundOp Craft(ContainerId[] chests, bool failLast)
    {
        var parts = new List<Operation>();
        foreach (var chest in chests)
        {
            parts.Add(new ItemDestroyOp(new SlotRef(chest, 0), CraftingFixture.Stone, 20, ItemSink.Craft));
        }

        parts.Add(new ItemCreateOp(new SlotRef(Bag, 0), CraftingFixture.Gem, 5, 5, ItemSource.Craft));
        parts.Add(CraftingFixture.Discover(Player1, CraftingFixture.Gem));
        if (failLast)
        {
            parts.Add(new ItemDestroyOp(new SlotRef(Bag, 5), CraftingFixture.Stone, 1, ItemSink.Craft));
        }

        return new CompoundOp(parts);
    }
}
