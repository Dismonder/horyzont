using Horyzont.Core.Content;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// The survival content of the hazard tests (K6-34): the items of <see cref="SurvivalContentFactory.HazardItemKeys"/> with their suit statistics, built from the records because the
/// loader reads <c>suitRefill</c> and <c>suitModule</c> only in step B3. The hazard profiles (<c>hazards.json</c>) come with B1.
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

    /// <summary>The content: the test items of the food world, then the refills, the modules and the generator fuel.</summary>
    /// <param name="map">Replaces an item definition (for tests with their own modules); null keeps them all.</param>
    /// <returns>The content.</returns>
    public static SurvivalContent Create(Func<ItemDef, ItemDef>? map = null)
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
        return new SurvivalContent(new ItemCatalog(map is null ? items : [.. items.Select(map)]), [], new StartKitDef(40, 10, 7, []), []);
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
