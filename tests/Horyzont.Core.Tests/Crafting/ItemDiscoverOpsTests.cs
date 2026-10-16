using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32, contract of operation 43 (<see cref="ItemDiscoverOp"/>). Every refusal starts from the accepted counterpart.</summary>
public sealed class ItemDiscoverOpsTests
{
    [Fact]
    public void Discover_NewType_SetsBitAndCreatesRecord()
    {
        var f = new CraftingFixture();
        Assert.Equal(0, f.State.Progress.Count);

        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));

        Assert.Equal(1, f.State.Progress.Count);
        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Null(progress.RespawnBed);
        Assert.True(progress.DiscoveredItems.Contains(CraftingFixture.Stone));
        Assert.False(progress.DiscoveredItems.Contains(CraftingFixture.Soil));
        Assert.Equal(1, progress.DiscoveredItems.Count);

        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Soil));
        Assert.True(f.State.Progress.TryGet(Player1, out var both));
        Assert.Equal(2, both.DiscoveredItems.Count);
        Assert.Equal(1, f.State.Progress.Count);
    }

    [Fact]
    public void Discover_PlayerWithRespawnPoint_KeepsTheBed()
    {
        var f = new CraftingFixture();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);

        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Gem));

        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Equal(BedAt(Horyzont.Testing.FunctionalBlocks.BedCell), progress.RespawnBed);
        Assert.True(progress.DiscoveredItems.Contains(CraftingFixture.Gem));
    }

    [Fact]
    public void Discover_AlreadyDiscovered_Fails()
    {
        var f = new CraftingFixture();
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));

        // The accepted counterpart: another type. The same type again changes nothing.
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Gem));
        f.AssertFails(CraftingFixture.Discover(Player1, CraftingFixture.Stone));
        f.AssertFails(CraftingFixture.Discover(Player1, CraftingFixture.Gem));
    }

    [Fact]
    public void Discover_UnknownPlayer_Fails()
    {
        var f = new CraftingFixture();
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));

        f.AssertFails(CraftingFixture.Discover(new PlayerId(99), CraftingFixture.Soil));
        Assert.Equal(1, f.State.Progress.Count);
    }

    [Fact]
    public void Discover_TypeNotInRegistry_Fails()
    {
        var f = new CraftingFixture();
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));

        // The registry of the test world has the ids 1 to 4.
        f.AssertFails(CraftingFixture.Discover(Player1, new ItemTypeId(5)));
        f.AssertFails(CraftingFixture.Discover(Player1, new ItemTypeId(4095)));
    }

    [Fact]
    public void Discover_TypeZero_Fails()
    {
        var f = new CraftingFixture();
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));

        f.AssertFails(CraftingFixture.Discover(Player1, ItemTypeId.None));
    }

    [Fact]
    public void Discover_InCompoundWithFailingComponent_RolledBack()
    {
        var f = new CraftingFixture();
        var create = new ItemCreateOp(new SlotRef(Bag, 0), CraftingFixture.Stone, 1, 500, ItemSource.Admin);
        var destroy = new ItemDestroyOp(new SlotRef(Bag, 0), CraftingFixture.Stone, 1, ItemSink.Craft);
        var discover = CraftingFixture.Discover(Player1, CraftingFixture.Gem);

        // The accepted counterpart: the destroy finds the item that the create put there.
        var failing = new CompoundOp([discover, destroy]);
        f.Apply(new CompoundOp([create, discover, destroy]));
        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.True(progress.DiscoveredItems.Contains(CraftingFixture.Gem));

        var g = new CraftingFixture();
        Assert.False(g.Blocks.TryApplyRaw(failing, out _, out _, out var index));
        Assert.Equal(1, index);
        g.AssertFails(failing);
        Assert.Equal(0, g.State.Progress.Count);
    }
}
