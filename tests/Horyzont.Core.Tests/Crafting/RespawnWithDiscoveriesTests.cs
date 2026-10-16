using Horyzont.Core.Operations;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32 D7: a progress record exists for a respawn point or for a discovery; operation 44 keeps the discoveries.</summary>
public sealed class RespawnWithDiscoveriesTests
{
    [Fact]
    public void RespawnClear_PlayerWithDiscoveries_KeepsRecord()
    {
        var f = new CraftingFixture();
        f.Blocks.SetUpBed(FunctionalBlocks.BedCell, Player1);
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Stone));
        Assert.True(f.State.Progress.TryGet(Player1, out var before));
        Assert.NotNull(before.RespawnBed);

        f.Apply(new RespawnPointSetOp(Player1, null));

        Assert.Equal(1, f.State.Progress.Count);
        Assert.True(f.State.Progress.TryGet(Player1, out var after));
        Assert.Null(after.RespawnBed);
        Assert.True(after.DiscoveredItems.Contains(CraftingFixture.Stone));
    }

    [Fact]
    public void RespawnSet_KeepsDiscoveries()
    {
        var f = new CraftingFixture();
        f.Apply(CraftingFixture.Discover(Player1, CraftingFixture.Gem));
        Assert.True(f.State.Progress.TryGet(Player1, out var before));
        Assert.Null(before.RespawnBed);

        f.Blocks.SetUpBed(FunctionalBlocks.BedCell, Player1);

        Assert.True(f.State.Progress.TryGet(Player1, out var after));
        Assert.Equal(BedAt(FunctionalBlocks.BedCell), after.RespawnBed);
        Assert.True(after.DiscoveredItems.Contains(CraftingFixture.Gem));
    }

    [Fact]
    public void RespawnClear_PlayerWithoutDiscoveries_RemovesRecord()
    {
        var f = new CraftingFixture();
        f.Blocks.SetUpBed(FunctionalBlocks.BedCell, Player1);
        Assert.Equal(1, f.State.Progress.Count);

        f.Apply(new RespawnPointSetOp(Player1, null));

        Assert.Equal(0, f.State.Progress.Count);
        Assert.False(f.State.Progress.TryGet(Player1, out _));
    }
}
