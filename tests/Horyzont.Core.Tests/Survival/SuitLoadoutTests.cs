using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Survival;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-34, D10: the capacity and the drain multipliers of the suit from the modules in the equipment.</summary>
public sealed class SuitLoadoutTests
{
    private static readonly HazardRules _rules = HazardRules.Default;
    private static readonly SuitModuleTable _modules = HazardTestContent.Modules(WorldFactory.CreateWithHazards());

    private static ItemStack One(ItemTypeId type) => new(type, 1);

    private static ItemStack[] Equipment(params (int Slot, ItemTypeId Type)[] items)
    {
        var slots = new ItemStack[EquipmentLayout.SlotCount];
        foreach (var (slot, type) in items)
        {
            slots[slot] = One(type);
        }

        return slots;
    }

    [Fact]
    public void Capacity_IsWholeNumber()
    {
        var stats = SuitLoadout.Compute(Equipment((4, HazardWorld.Tank)), _modules, _rules);
        foreach (var resource in Enum.GetValues<SuitResource>())
        {
            Assert.Equal(MathF.Truncate(stats.Capacity[resource]), stats.Capacity[resource]);
        }
    }

    [Fact]
    public void StackOfThreeInModuleSlot_CountsOnce()
    {
        var equipment = Equipment((4, HazardWorld.Tank));
        var once = SuitLoadout.Compute(equipment, _modules, _rules);
        equipment[4] = new ItemStack(HazardWorld.Tank, 3);
        Assert.Equal(once, SuitLoadout.Compute(equipment, _modules, _rules));
    }

    [Fact]
    public void NoModules_Base100()
    {
        var stats = SuitLoadout.Compute(Equipment(), _modules, _rules);

        Assert.Equal(new SuitResources(100f, 100f, 100f, 100f, 100f), stats.Capacity);
        Assert.Equal(new SuitResources(1f, 1f, 1f, 1f, 1f), stats.Drain);

        // The base capacity is a rule.
        Assert.Equal(new SuitResources(80f, 80f, 80f, 80f, 80f), SuitLoadout.Compute(Equipment(), _modules, _rules with { SuitBaseCapacity = 80 }).Capacity);
    }

    [Fact]
    public void CapacityModules_Sum()
    {
        var one = SuitLoadout.Compute(Equipment((4, HazardWorld.Tank)), _modules, _rules);
        var three = SuitLoadout.Compute(Equipment((4, HazardWorld.Tank), (5, HazardWorld.Tank), (6, HazardWorld.Tank)), _modules, _rules);

        // The tank gives +100 oxygen and nothing else.
        Assert.Equal(new SuitResources(100f, 100f, 100f, 100f, 200f), one.Capacity);
        Assert.Equal(new SuitResources(100f, 100f, 100f, 100f, 400f), three.Capacity);
        Assert.Equal(new SuitResources(1f, 1f, 1f, 1f, 1f), three.Drain);

        // The capacity never exceeds the maximum of the formats (1000): three modules of +500 on 100 base would be 1600.
        var big = HazardTestContent.Modules(
            WorldFactory.CreateWithHazards(),
            item => item.Key == SurvivalContentFactory.TankModuleKey ? item with { SuitModule = new SuitModuleDef([new SuitModuleEffect(SuitResource.Oxygen, 500, 1f)]) } : item);
        var capped = SuitLoadout.Compute(Equipment((4, HazardWorld.Tank), (5, HazardWorld.Tank), (6, HazardWorld.Tank)), big, _rules);
        Assert.Equal(1000f, capped.Capacity.Oxygen);
        Assert.Equal(1000f, SuitLoadout.Compute(Equipment((4, HazardWorld.Tank), (5, HazardWorld.Tank)), big, _rules).Capacity.Oxygen);
    }

    [Fact]
    public void DrainMultipliers_Multiply_FloorQuarter()
    {
        var one = SuitLoadout.Compute(Equipment((4, HazardWorld.Thermal)), _modules, _rules);
        var two = SuitLoadout.Compute(Equipment((4, HazardWorld.Thermal), (5, HazardWorld.Thermal)), _modules, _rules);
        var three = SuitLoadout.Compute(Equipment((4, HazardWorld.Thermal), (5, HazardWorld.Thermal), (6, HazardWorld.Thermal)), _modules, _rules);

        // The thermal module multiplies the drain of the heating and of the cooling by 0.6; the product of two is 0.36; three would be 0.216, which the floor 0.25 raises.
        Assert.Equal(0.6f, one.Drain.Heating, 5);
        Assert.Equal(0.6f, one.Drain.Cooling, 5);
        Assert.Equal(0.36f, two.Drain.Heating, 5);
        Assert.Equal(0.36f, two.Drain.Cooling, 5);
        Assert.Equal(0.25f, three.Drain.Heating, 5);
        Assert.Equal(0.25f, three.Drain.Cooling, 5);

        // The other resources are untouched, and so are the capacities.
        Assert.Equal(1f, three.Drain.Filter);
        Assert.Equal(1f, three.Drain.Shield);
        Assert.Equal(1f, three.Drain.Oxygen);
        Assert.Equal(new SuitResources(100f, 100f, 100f, 100f, 100f), three.Capacity);

        // The floor is a rule: a floor of 0.3 lifts the product 0.216, and a floor of 0.1 below it leaves the product.
        Assert.Equal(0.3f, SuitLoadout.Compute(Equipment((4, HazardWorld.Thermal), (5, HazardWorld.Thermal), (6, HazardWorld.Thermal)), _modules, _rules with { MinDrainMultiplier = 0.3f }).Drain.Heating, 5);
        Assert.Equal(0.216f, SuitLoadout.Compute(Equipment((4, HazardWorld.Thermal), (5, HazardWorld.Thermal), (6, HazardWorld.Thermal)), _modules, _rules with { MinDrainMultiplier = 0.1f }).Drain.Heating, 5);
    }

    [Fact]
    public void MixedModules_EachOnItsOwnResource()
    {
        var stats = SuitLoadout.Compute(Equipment((4, HazardWorld.Tank), (6, HazardWorld.Thermal)), _modules, _rules);

        Assert.Equal(new SuitResources(100f, 100f, 100f, 100f, 200f), stats.Capacity);
        Assert.Equal(0.6f, stats.Drain.Heating, 5);
        Assert.Equal(1f, stats.Drain.Oxygen);
    }

    [Fact]
    public void OnlyModuleSlotsCount()
    {
        // A module lying in the helmet, torso, legs or backpack slot does nothing (only the slots 4 to 6 are module slots); the accepted counterpart is slot 4.
        Assert.Equal(200f, SuitLoadout.Compute(Equipment((4, HazardWorld.Tank)), _modules, _rules).Capacity.Oxygen);
        Assert.Equal(200f, SuitLoadout.Compute(Equipment((6, HazardWorld.Tank)), _modules, _rules).Capacity.Oxygen);

        for (var slot = 0; slot < 4; slot++)
        {
            var stats = SuitLoadout.Compute(Equipment((slot, HazardWorld.Tank), (slot == 0 ? 1 : 0, HazardWorld.Thermal)), _modules, _rules);
            Assert.Equal(100f, stats.Capacity.Oxygen);
            Assert.Equal(1f, stats.Drain.Heating);
        }
    }

    [Fact]
    public void UnknownType_Ignored()
    {
        // Items that are not modules (stone, a heat cell), a type that is not in the registry and empty slots count for nothing; the tank in the same equipment still counts.
        var equipment = Equipment((4, HazardWorld.Stone), (5, HazardWorld.HeatCell), (6, new ItemTypeId(9999)));
        Assert.Equal(new SuitStats(new SuitResources(100f, 100f, 100f, 100f, 100f), new SuitResources(1f, 1f, 1f, 1f, 1f)), SuitLoadout.Compute(equipment, _modules, _rules));

        equipment[4] = One(HazardWorld.Tank);
        Assert.Equal(200f, SuitLoadout.Compute(equipment, _modules, _rules).Capacity.Oxygen);

        // A table of another world and a shorter container (fewer than seven slots) are no reason to throw.
        Assert.Equal(100f, SuitLoadout.Compute([], _modules, _rules).Capacity.Oxygen);
        Assert.Equal(200f, SuitLoadout.Compute(equipment.AsSpan(0, 5), _modules, _rules).Capacity.Oxygen);
    }
}
