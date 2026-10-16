using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Crafting;

public sealed class RecipeListTests
{
    private static string[] KeysOf(IReadOnlyList<RecipeView> views) => [.. views.Select(v => v.Recipe.Key)];

    [Fact]
    public void Hands_ShowsKnownHandRecipesOnly()
    {
        var fixture = new CraftingClientFixture();
        Assert.Empty(fixture.Recipes.Visible(StationKind.Hands));

        // Ingots are an ingredient of workbench recipes only: the recipe of the hands stays hidden.
        fixture.Know([SurvivalContentFactory.IngotKey]);
        Assert.Empty(fixture.Recipes.Visible(StationKind.Hands));

        fixture.Know([SurvivalContentFactory.StoneKey]);
        Assert.Equal(["t:hand"], KeysOf(fixture.Recipes.Visible(StationKind.Hands)));
        Assert.Empty(fixture.Recipes.Visible(StationKind.Workbench));
    }

    [Fact]
    public void Furnace_ShowsFurnaceRecipes()
    {
        var fixture = new CraftingClientFixture();

        // Peat is fuel only: discovering it shows nothing.
        fixture.Know([SurvivalContentFactory.PeatKey]);
        Assert.Empty(fixture.Recipes.Visible(StationKind.Furnace));

        fixture.Know([SurvivalContentFactory.PeatKey, SurvivalContentFactory.OreKey]);
        Assert.Equal(["t:smelt"], KeysOf(fixture.Recipes.Visible(StationKind.Furnace)));

        fixture.Know([SurvivalContentFactory.OreKey, SurvivalContentFactory.SoilKey]);
        Assert.Equal(["t:smelt", "t:smelt2"], KeysOf(fixture.Recipes.Visible(StationKind.Furnace)));
        Assert.Equal(["t:refine"], KeysOf(fixture.Recipes.Visible(StationKind.Refinery)));
    }

    [Fact]
    public void KeyArrives_RecipeAppears()
    {
        var fixture = new CraftingClientFixture();
        fixture.Know([SurvivalContentFactory.PlateKey]);
        Assert.Empty(fixture.Recipes.Visible(StationKind.Fabricator));
        Assert.False(fixture.Recipes.IsKnown(fixture.Recipe("t:fab")));

        fixture.Know([SurvivalContentFactory.PlateKey], SurvivalContentFactory.Boss2Key);

        Assert.Equal(["t:fab"], KeysOf(fixture.Recipes.Visible(StationKind.Fabricator)));
        Assert.True(fixture.Recipes.IsKnown(fixture.Recipe("t:fab")));
    }

    [Fact]
    public void HaveCounts_BagPlusHotbar()
    {
        var fixture = new CraftingClientFixture();
        fixture.Know([SurvivalContentFactory.OreKey]);
        fixture.Put(CraftingClientFixture.Bag(0), SurvivalContentFactory.OreKey, 3);
        fixture.State(CraftingClientFixture.InputId, Horyzont.Core.Items.ContainerKind.Chest, 4, (0, SurvivalContentFactory.OreKey, 50));
        fixture.Put(new Horyzont.Core.Items.SlotRef(CraftingClientFixture.HotbarId, 3), SurvivalContentFactory.OreKey, 2);

        var smelt = Assert.Single(fixture.Recipes.Visible(StationKind.Furnace));

        // Five ore in the bag and the hotbar (the 50 in the chest are not counted); no peat for the fuel.
        Assert.Equal(new IngredientView(SurvivalContentFactory.OreKey, 2, 5), Assert.Single(smelt.Ingredients));
        Assert.Equal(new IngredientView(SurvivalContentFactory.PeatKey, 1, 0), Assert.Single(smelt.Fuel));
        Assert.False(smelt.Affordable);

        fixture.Put(CraftingClientFixture.Bag(1), SurvivalContentFactory.PeatKey, 1);

        var again = Assert.Single(fixture.Recipes.Visible(StationKind.Furnace));
        Assert.Equal(1, Assert.Single(again.Fuel).Have);
        Assert.True(again.Affordable);
    }

    [Fact]
    public void Affordable_NeedsEveryIngredient()
    {
        var fixture = new CraftingClientFixture();
        fixture.Know([SurvivalContentFactory.StoneKey]);

        Assert.False(Assert.Single(fixture.Recipes.Visible(StationKind.Hands)).Affordable);

        fixture.Put(CraftingClientFixture.Bag(0), SurvivalContentFactory.StoneKey, 1);
        Assert.False(Assert.Single(fixture.Recipes.Visible(StationKind.Hands)).Affordable);

        fixture.Put(CraftingClientFixture.Bag(1), SurvivalContentFactory.StoneKey, 1);
        Assert.True(Assert.Single(fixture.Recipes.Visible(StationKind.Hands)).Affordable);
    }

    [Fact]
    public void IsKnown_UsesClientKeyMapping()
    {
        // The ids of message 49 are shifted against the order of the content: the list resolves keys through the client inventory.
        var content = SurvivalContentFactory.CreateTestWithRecipes();
        var inventory = new ClientInventory(content.Items, _ => { }, NullLogger<ClientInventory>.Instance);
        inventory.OnMessage(new ItemRegistry([.. SurvivalContentFactory.CraftingItemKeys.Select((key, i) => new RegistryEntry((ushort)(i + 100), key))]));
        var progress = new ClientProgress();
        var recipes = new RecipeList(content, inventory, progress);
        var hand = content.TryGetRecipe("t:hand", out var recipe) ? recipe : throw new InvalidOperationException("no recipe t:hand");

        Assert.True(inventory.TryGetTypeId(SurvivalContentFactory.StoneKey, out var stone));
        Assert.True(stone.Value >= 100);
        Assert.False(recipes.IsKnown(hand));

        progress.Apply(new PlayerProgressState(ItemTypeBitSet.Empty.With(stone), []));
        Assert.True(recipes.IsKnown(hand));

        // The id the content order would give is not the discovered one.
        progress.Apply(new PlayerProgressState(ItemTypeBitSet.Empty.With(CraftingClientFixture.Item(SurvivalContentFactory.StoneKey)), []));
        Assert.False(recipes.IsKnown(hand));
    }
}
