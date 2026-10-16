using Horyzont.Core.Content;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// The survival content of the food tests (K6-33): the content of <see cref="SurvivalContentFactory.CreateTest"/> plus the four food types of
/// <see cref="SurvivalContentFactory.FoodItemKeys"/>. The loader reads the <c>food</c> field only in step B3, so the items are built from the records here.
/// </summary>
internal static class FoodContent
{
    /// <summary>Ticks per second of the tests.</summary>
    public const int TickRate = 60;

    /// <summary>Berries: 10 health, 20 stamina, 0.5 regeneration, 600 s.</summary>
    public static readonly FoodDef Berries = new(10, 20, 0.5f, 600);

    /// <summary>Jam: 25 health, 35 stamina, 1.0 regeneration, 1200 s.</summary>
    public static readonly FoodDef Jam = new(25, 35, 1.0f, 1200);

    /// <summary>Broth: 15 health, no stamina, 1.5 regeneration, 900 s.</summary>
    public static readonly FoodDef Broth = new(15, 0, 1.5f, 900);

    /// <summary>Bread: 20 health, 10 stamina, 0.25 regeneration, 1800 s.</summary>
    public static readonly FoodDef Bread = new(20, 10, 0.25f, 1800);

    /// <summary>The ticks a food lasts.</summary>
    /// <param name="food">The food.</param>
    /// <returns>Duration in ticks at <see cref="TickRate"/>.</returns>
    public static ulong DurationTicks(FoodDef food) => food.DurationSeconds * (ulong)TickRate;

    /// <summary>The content: tool, gem, stone and soil of the test content, then the four foods.</summary>
    /// <returns>The content.</returns>
    public static SurvivalContent Create()
    {
        var basic = SurvivalContentFactory.CreateTest();
        ItemDef[] items =
        [
            .. basic.Items.Items,
            FoodItem(SurvivalContentFactory.BerriesKey, Berries),
            FoodItem(SurvivalContentFactory.JamKey, Jam),
            FoodItem(SurvivalContentFactory.BrothKey, Broth),
            FoodItem(SurvivalContentFactory.BreadKey, Bread),
        ];
        return new SurvivalContent(new ItemCatalog(items), [.. basic.Yields], basic.StartKit);
    }

    /// <summary>The food table of <see cref="Create"/> for the item types of a world.</summary>
    /// <param name="state">The world, e.g. of <see cref="WorldFactory.CreateWithFood"/>.</param>
    /// <returns>The table.</returns>
    public static FoodTable Table(WorldState state) => FoodTable.Build(Create(), state.ItemTypes);

    private static ItemDef FoodItem(string key, FoodDef food) => new(key, ItemCategory.Food, 20, ItemFlags.Food, 0, EquipmentSlot.None, food);
}
