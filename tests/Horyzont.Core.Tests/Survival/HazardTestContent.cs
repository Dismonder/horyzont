using Horyzont.Core.Content;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// The survival content of the hazard tests (K6-34): the items of <see cref="SurvivalContentFactory.HazardItemKeys"/> with their suit statistics, the hazard profiles of D24 and the
/// oxygen generator, built from the records because the loader reads <c>suitRefill</c>, <c>suitModule</c> and <c>hazards.json</c> only in step B3; one hands recipe makes crafting available.
/// </summary>
internal static class HazardTestContent
{
    /// <summary>Ticks per second of the tests.</summary>
    public const int TickRate = 60;

    /// <summary>The heat cell: 40 points of heating.</summary>
    public static readonly SuitRefillDef HeatRefill = new(SuitResource.Heating, 40);

    /// <summary>The oxygen canister: 50 points of oxygen.</summary>
    public static readonly SuitRefillDef OxygenRefill = new(SuitResource.Oxygen, 50);

    /// <summary>The oxygen tank module: +100 oxygen capacity.</summary>
    public static readonly SuitModuleDef Tank = new([new SuitModuleEffect(SuitResource.Oxygen, 100, 1.0f)]);

    /// <summary>The thermal module: heating and cooling drain x0.6.</summary>
    public static readonly SuitModuleDef Thermal = new([new SuitModuleEffect(SuitResource.Heating, 0, 0.6f), new SuitModuleEffect(SuitResource.Cooling, 0, 0.6f)]);

    /// <summary>Key of the profile of the home planet (body 0).</summary>
    public const string TemperateKey = "core:temperate";

    /// <summary>Key of the profile of the moon (body 1).</summary>
    public const string MoonKey = "core:barren_moon";

    /// <summary>Key of the profile of the toxic planet (body 2).</summary>
    public const string ToxicKey = "core:toxic";

    /// <summary>Key of the profile of the frozen planet (body 3).</summary>
    public const string FrozenKey = "core:frozen";

    /// <summary>Key of the profile of space.</summary>
    public const string SpaceKey = "core:space";

    /// <summary>Seconds one fuel cell burns in the test content.</summary>
    public const int SecondsPerCell = 10;

    /// <summary>Ticks one fuel cell burns in the test content at <see cref="TickRate"/>.</summary>
    public const ulong TicksPerCell = SecondsPerCell * TickRate;

    /// <summary>Cold at night 0.25 and twice that from 150 m up.</summary>
    public static readonly HazardProfileDef Temperate = new(
        TemperateKey, default, new HazardRates(0.25f, 0f, 0f, 0f, 0f), [new HazardZoneDef(150f, new HazardRates(2f, 1f, 1f, 1f, 1f))]);

    /// <summary>The moon: heat 0.3 and radiation 0.3 by day, cold 0.4 by night, vacuum 0.5 all the time.</summary>
    public static readonly HazardProfileDef Moon = new(MoonKey, new HazardRates(0f, 0.3f, 0f, 0.3f, 0.5f), new HazardRates(0.4f, 0f, 0f, 0f, 0.5f), []);

    /// <summary>The toxic planet: heat 0.3 by day, toxins 0.4 all the time.</summary>
    public static readonly HazardProfileDef Toxic = new(ToxicKey, new HazardRates(0f, 0.3f, 0.4f, 0f, 0f), new HazardRates(0f, 0f, 0.4f, 0f, 0f), []);

    /// <summary>The frozen planet: cold 0.5 by day and 1 by night, radiation 0.15 by day.</summary>
    public static readonly HazardProfileDef Frozen = new(FrozenKey, new HazardRates(0.5f, 0f, 0f, 0.15f, 0f), new HazardRates(1f, 0f, 0f, 0f, 0f), []);

    /// <summary>Space: cold 0.3, radiation 0.3 and vacuum 0.5, day and night.</summary>
    public static readonly HazardProfileDef Space = new(SpaceKey, new HazardRates(0.3f, 0f, 0f, 0.3f, 0.5f), new HazardRates(0.3f, 0f, 0f, 0.3f, 0.5f), []);

    /// <summary>The hazard content of D24: the profiles of bodies 0 to 3, the default profile, the profile of space and the generator that burns the fuel pack for <see cref="SecondsPerCell"/> seconds a cell.</summary>
    public static readonly HazardContent Hazards = new(
        [Temperate, Moon, Toxic, Frozen, Space],
        [new HazardBodyDef(0, TemperateKey), new HazardBodyDef(1, MoonKey), new HazardBodyDef(2, ToxicKey), new HazardBodyDef(3, FrozenKey)],
        TemperateKey,
        SpaceKey,
        new OxygenGeneratorDef(SurvivalContentFactory.FuelPackKey, SecondsPerCell));

    /// <summary>The recipe of the hands that makes a heat cell of two stones.</summary>
    public static readonly RecipeDef CutRecipe = new(
        "t:cut", StationKind.Hands, 1, [new RecipeEntry(SurvivalContentFactory.StoneKey, 2)], [], new RecipeEntry(SurvivalContentFactory.HeatCellKey, 1), 0, null);

    /// <summary>The content: the test items of the food world, then the refills, the modules and the generator fuel.</summary>
    /// <param name="map">Replaces an item definition (for tests with their own modules); null keeps them all.</param>
    /// <param name="hazards">Whether the content has the hazards and the generator (false is the content of K6-33: the items are there, the rules are off).</param>
    /// <returns>The content.</returns>
    public static SurvivalContent Create(Func<ItemDef, ItemDef>? map = null, bool hazards = true)
    {
        var items = new ItemDef[]
        {
            new(SurvivalContentFactory.ToolKey, ItemCategory.Tool, 1, ItemFlags.Tool, 1, EquipmentSlot.None),
            new(SurvivalContentFactory.GemKey, ItemCategory.Component, 5, ItemFlags.None, 0, EquipmentSlot.None),
            new(SurvivalContentFactory.StoneKey, ItemCategory.Resource, 500, ItemFlags.Bulk, 0, EquipmentSlot.None),
            new(SurvivalContentFactory.SoilKey, ItemCategory.Resource, 500, ItemFlags.Bulk, 0, EquipmentSlot.None),
            new(SurvivalContentFactory.BerriesKey, ItemCategory.Food, 20, ItemFlags.Food, 0, EquipmentSlot.None, FoodContent.Berries),
            new(SurvivalContentFactory.JamKey, ItemCategory.Food, 20, ItemFlags.Food, 0, EquipmentSlot.None, FoodContent.Jam),
            new(SurvivalContentFactory.BrothKey, ItemCategory.Food, 20, ItemFlags.Food, 0, EquipmentSlot.None, FoodContent.Broth),
            new(SurvivalContentFactory.BreadKey, ItemCategory.Food, 20, ItemFlags.Food, 0, EquipmentSlot.None, FoodContent.Bread),
            new(SurvivalContentFactory.HeatCellKey, ItemCategory.Component, 100, ItemFlags.None, 0, EquipmentSlot.None, SuitRefill: HeatRefill),
            new(SurvivalContentFactory.OxygenCanisterKey, ItemCategory.Component, 100, ItemFlags.None, 0, EquipmentSlot.None, SuitRefill: OxygenRefill),
            new(SurvivalContentFactory.TankModuleKey, ItemCategory.Armor, 1, ItemFlags.Armor, 0, EquipmentSlot.Module, SuitModule: Tank),
            new(SurvivalContentFactory.ThermalModuleKey, ItemCategory.Armor, 1, ItemFlags.Armor, 0, EquipmentSlot.Module, SuitModule: Thermal),
            new(SurvivalContentFactory.FuelPackKey, ItemCategory.Component, 100, ItemFlags.None, 0, EquipmentSlot.None),
        };
        return new SurvivalContent(new ItemCatalog(map is null ? items : [.. items.Select(map)]), [], new StartKitDef(40, 10, 7, []), [], [CutRecipe], hazards ? Hazards : null);
    }

    /// <summary>The module table of <see cref="Create"/> for the item types of a world.</summary>
    /// <param name="state">The world, e.g. of <see cref="WorldFactory.CreateWithHazards"/>.</param>
    /// <returns>The table.</returns>
    public static SuitModuleTable Modules(WorldState state) => SuitModuleTable.Build(Create(), state.ItemTypes);

    /// <summary>The module table of <see cref="Create"/> with replaced items.</summary>
    /// <param name="state">The world.</param>
    /// <param name="map">Replaces an item definition.</param>
    /// <returns>The table.</returns>
    public static SuitModuleTable Modules(WorldState state, Func<ItemDef, ItemDef> map) => SuitModuleTable.Build(Create(map), state.ItemTypes);
}
