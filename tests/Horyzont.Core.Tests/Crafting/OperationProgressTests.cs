using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32 D11: <see cref="OperationProgress.Collect"/> sees the discoveries and keys of a compound.</summary>
public sealed class OperationProgressTests
{
    private static readonly ItemCreateOp _create = new(new SlotRef(Bag, 0), CraftingFixture.Stone, 1, 500, ItemSource.Craft);

    [Fact]
    public void Collect_DiscoverInCompound_ReturnsPlayer()
    {
        var players = new List<PlayerId>();

        OperationProgress.Collect(CraftingFixture.Discover(Player2, CraftingFixture.Gem), players, out var keysAlone);
        Assert.Equal([Player2], players);
        Assert.False(keysAlone);

        players.Clear();
        OperationProgress.Collect(
            new CompoundOp([_create, CraftingFixture.Discover(Player2, CraftingFixture.Gem), CraftingFixture.Discover(Player2, CraftingFixture.Soil)]),
            players,
            out var keys);
        Assert.Equal([Player2], players);
        Assert.False(keys);

        players.Clear();
        OperationProgress.Collect(
            new CompoundOp([CraftingFixture.Discover(Player2, CraftingFixture.Gem), CraftingFixture.Discover(Player1, CraftingFixture.Gem)]), players, out _);
        Assert.Equal([Player2, Player1], players);
    }

    [Fact]
    public void Collect_ClearsListFirst()
    {
        // The list is reused by the server: Collect starts by clearing it (review A1, 4-13).
        var players = new List<PlayerId> { Player1 };

        OperationProgress.Collect(CraftingFixture.Grant("test:boss"), players, out var keys);

        Assert.True(keys);
        Assert.Empty(players);

        players.Add(Player1);
        OperationProgress.Collect(CraftingFixture.Discover(Player2, CraftingFixture.Gem), players, out _);
        Assert.Equal([Player2], players);
    }

    [Fact]
    public void Collect_GrantKey_KeysChanged()
    {
        var players = new List<PlayerId>();

        OperationProgress.Collect(CraftingFixture.Grant("test:boss"), players, out var alone);
        OperationProgress.Collect(new CompoundOp([_create, CraftingFixture.Grant("test:boss")]), players, out var inCompound);

        Assert.True(alone);
        Assert.True(inCompound);
        Assert.Empty(players);
    }

    [Fact]
    public void Collect_ItemOnlyCompound_Nothing()
    {
        var players = new List<PlayerId>();

        OperationProgress.Collect(CraftingFixture.Discover(Player1, CraftingFixture.Gem), players, out _);
        Assert.Single(players);

        players.Clear();
        OperationProgress.Collect(new CompoundOp([_create, new ItemDestroyOp(new SlotRef(Bag, 0), CraftingFixture.Stone, 1, ItemSink.Craft)]), players, out var keys);
        OperationProgress.Collect(_create, players, out var keysAlone);

        Assert.Empty(players);
        Assert.False(keys);
        Assert.False(keysAlone);
    }
}
