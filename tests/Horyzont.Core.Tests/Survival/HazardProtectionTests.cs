using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-34, D15: which hazards a shelter, an oxygen generator and the life support of a ship seat cancel.</summary>
public sealed class HazardProtectionTests
{
    private static readonly HazardRates _all = new(1f, 2f, 3f, 4f, 5f);

    [Fact]
    public void Shelter_BlocksColdHeatToxinsOnly()
    {
        // Cold, heat and toxins are zero; radiation and vacuum pass (spec l. 138).
        Assert.Equal(new HazardRates(0f, 0f, 0f, 4f, 5f), HazardProtection.Apply(_all, true, false, false));
    }

    [Fact]
    public void Generator_BlocksVacuumOnly()
    {
        Assert.Equal(new HazardRates(1f, 2f, 3f, 4f, 0f), HazardProtection.Apply(_all, false, true, false));

        // Both together cover everything but radiation.
        Assert.Equal(new HazardRates(0f, 0f, 0f, 4f, 0f), HazardProtection.Apply(_all, true, true, false));
    }

    [Fact]
    public void LifeSupport_BlocksAll()
    {
        Assert.Equal(default, HazardProtection.Apply(_all, false, false, true));
        Assert.Equal(default, HazardProtection.Apply(_all, true, true, true));
    }

    [Fact]
    public void Nothing_ChangesNothing()
    {
        Assert.Equal(_all, HazardProtection.Apply(_all, false, false, false));
        Assert.Equal(default, HazardProtection.Apply(default, true, true, true));

        // A hazard that is not present stays absent and the rates that pass keep their values.
        Assert.Equal(new HazardRates(0f, 0f, 0f, 0.5f, 0f), HazardProtection.Apply(new HazardRates(0.4f, 0f, 0.25f, 0.5f, 0.1f), true, true, false));
    }
}
