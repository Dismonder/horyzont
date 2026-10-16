using Horyzont.Core.Operations;

using Horyzont.Core.Tests.BlockEntities;

using static Horyzont.Core.Tests.Survival.FoodWorld;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-33, D4: a record of progress exists if and only if a field differs from its default; food counts, so clearing the respawn point must not drop it.</summary>
public sealed class RespawnWithFoodTests
{
    [Fact]
    public void RespawnClear_NothingElse_RemovesRecord()
    {
        // The unchanged K6-30 behaviour, and the counterpart of the next test: a record with only a respawn point goes with it.
        var f = new FoodWorld();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        Assert.Equal(1, f.State.Progress.Count);

        f.Apply(new RespawnPointSetOp(Player1, null));

        Assert.Equal(0, f.State.Progress.Count);
    }

    [Fact]
    public void RespawnClear_PlayerWithOnlyFood_KeepsRecord()
    {
        var f = new FoodWorld();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(Set(Player1, Slot(Berries, 100)));

        f.Apply(new RespawnPointSetOp(Player1, null));

        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Null(progress.RespawnBed);
        Assert.Equal(Slots(Slot(Berries, 100)), progress.Food);
        Assert.Equal(1, f.State.Progress.Count);
    }

    [Fact]
    public void RespawnClear_FoodAndDiscoveries_KeepsBoth()
    {
        var f = new FoodWorld();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        f.Apply(Set(Player1, Slot(Berries, 100)));
        f.Apply(new ItemDiscoverOp(Player1, Stone));

        f.Apply(new RespawnPointSetOp(Player1, null));

        Assert.True(f.State.Progress.TryGet(Player1, out var progress));
        Assert.Null(progress.RespawnBed);
        Assert.Equal(Slots(Slot(Berries, 100)), progress.Food);
        Assert.True(progress.DiscoveredItems.Contains(Stone));
    }

    [Fact]
    public void RespawnSet_KeepsFood()
    {
        var f = new FoodWorld();
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell);
        f.Apply(Set(Player1, Slot(Berries, 100), Slot(Jam, 200)));
        Assert.True(f.State.Progress.TryGet(Player1, out var before));
        Assert.Null(before.RespawnBed);

        f.Apply(new RespawnPointSetOp(Player1, FunctionalBlockFixture.BedAt(Horyzont.Testing.FunctionalBlocks.BedCell)));

        Assert.True(f.State.Progress.TryGet(Player1, out var after));
        Assert.Equal(FunctionalBlockFixture.BedAt(Horyzont.Testing.FunctionalBlocks.BedCell), after.RespawnBed);
        Assert.Equal(Slots(Slot(Berries, 100), Slot(Jam, 200)), after.Food);
    }
}
