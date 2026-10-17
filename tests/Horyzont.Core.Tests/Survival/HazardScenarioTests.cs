using System.Numerics;

using Horyzont.Core.Content;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Survival.ShelterScenes;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// The pure functions of K6-34 on reference scenes (spec 03 lines 129 to 140, D9 to D15): the profile of the body from <see cref="HazardProfileResolver"/>, the protection from <see cref="ShelterEvaluator"/> and
/// <see cref="OxygenCoverage"/>, then <see cref="HazardField"/>, <see cref="HazardProtection"/> and <see cref="SuitStep"/> second by second at 60 ticks. The test of the acceptance of the package: a profile
/// of the planet uses up the right resource and the shelter, the generator and the seat protect from the right hazards. The hazards of the profiles are those of D24; the suit has the base capacity of 100 and no modules.
/// </summary>
public sealed class HazardScenarioTests
{
    private const int TickRate = 60;

    private static readonly SuitResources _full = new(100f, 100f, 100f, 100f, 100f);
    private static readonly SuitStats _stats = new(_full, new SuitResources(1f, 1f, 1f, 1f, 1f));

    /// <summary>Daylight 1 (noon) and 0 (night) as <see cref="HazardField.Rates"/> takes it.</summary>
    private const float Day = 1f;
    private const float Night = 0f;

    private static WorldState StarWorld() => WorldFactory.CreateWithStarSystem(77);

    /// <summary>The profile of a body of the star system on the ground (height 0).</summary>
    private static HazardProfileDef ProfileOfBody(int body)
    {
        var state = StarWorld();
        var planet = state.StarSystem!.Bodies.Single(b => b.BodyIndex == body).Planet;
        return HazardProfileResolver.Resolve(state, HazardTestContent.Hazards, planet, 0f);
    }

    private static bool ShelteredInAHut()
    {
        var f = Scene(Closed);
        return ShelterEvaluator.Evaluate(f.State, f.Terrain, WorldFactory.HomePlanet, Center, HazardRules.Default).Sheltered;
    }

    private static bool ShelteredInTheOpen() =>
        ShelterEvaluator.Evaluate(new SimFixture().State, new FakeTerrainService(), WorldFactory.HomePlanet, Center, HazardRules.Default).Sheltered;

    private static bool NextToARunningGenerator()
    {
        var w = new HazardWorld();
        w.SetUpGenerator(HazardWorld.GeneratorCell, w.Now);
        return OxygenCoverage.IsCovered(
            w.State, WorldFactory.HomePlanet, new Vector3(6f, 4002f, 1f), HazardWorld.Fuel, HazardTestContent.TicksPerCell, w.Now, HazardRules.Default);
    }

    /// <summary>The suit after <paramref name="seconds"/> of the profile in the given daylight and protection, from <paramref name="start"/>.</summary>
    private static (SuitResources Suit, float Damage) Live(
        HazardProfileDef profile, float daylight, bool sheltered, bool generator, bool lifeSupport, SuitResources start, int seconds)
    {
        var present = HazardField.Rates(profile, 0f, daylight, 1f);
        var effective = HazardProtection.Apply(present, sheltered, generator, lifeSupport);
        var suit = start;
        var damage = 0f;
        for (var i = 0; i < seconds * TickRate; i++)
        {
            var step = SuitStep.Advance(suit, effective, _stats, sheltered || generator || lifeSupport, HazardRules.Default, TickRate);
            suit = step.Next;
            damage += step.Damage;
        }

        return (suit, damage);
    }

    private static void AssertDrained(SuitResources after, SuitResource only, float amount)
    {
        foreach (var resource in Enum.GetValues<SuitResource>())
        {
            Assert.Equal(resource == only ? 100f - amount : 100f, after[resource], 2);
        }
    }

    [Fact]
    public void MoonVacuum_DrainsOxygenOnly()
    {
        // The moon at night in a shelter: the cold is kept out, the vacuum is not (0.5 a second), and there is no radiation by night. After 20 s oxygen is down by 10, the rest is at the capacity.
        var moon = ProfileOfBody(1);
        Assert.True(ShelteredInAHut());

        var (suit, damage) = Live(moon, Night, sheltered: true, generator: false, lifeSupport: false, _full, 20);

        AssertDrained(suit, SuitResource.Oxygen, 10f);
        Assert.Equal(0f, damage);

        // The twin on the open ground: the cold drains the heating as well.
        Assert.False(ShelteredInTheOpen());
        var open = Live(moon, Night, sheltered: false, generator: false, lifeSupport: false, _full, 20).Suit;
        Assert.Equal(100f - 8f, open.Heating, 2);
        Assert.Equal(100f - 10f, open.Oxygen, 2);
    }

    [Fact]
    public void HomeNight_DrainsHeatingOnly()
    {
        // The home planet by night in the open: the cold, 0.25 a second, and nothing else. After 40 s the heating is down by 10.
        var home = ProfileOfBody(0);

        var (suit, damage) = Live(home, Night, sheltered: false, generator: false, lifeSupport: false, _full, 40);

        AssertDrained(suit, SuitResource.Heating, 10f);
        Assert.Equal(0f, damage);

        // The twin by day: no hazard at all.
        Assert.Equal(_full, Live(home, Day, sheltered: false, generator: false, lifeSupport: false, _full, 40).Suit);
    }

    [Fact]
    public void ToxicPlanet_DrainsFilter()
    {
        // The toxic planet by night: toxins, 0.4 a second, and no heat by night. After 25 s the filter is down by 10.
        var toxic = ProfileOfBody(2);

        var (suit, damage) = Live(toxic, Night, sheltered: false, generator: false, lifeSupport: false, _full, 25);

        AssertDrained(suit, SuitResource.Filter, 10f);
        Assert.Equal(0f, damage);

        // The shelter keeps toxins out: the same night under a roof drains nothing.
        Assert.Equal(_full, Live(toxic, Night, sheltered: true, generator: false, lifeSupport: false, _full, 25).Suit);
    }

    [Fact]
    public void Radiation_IgnoresShelter()
    {
        // The moon at noon: heat 0.3 (cooling), radiation 0.3 (shield) and vacuum 0.5 (oxygen). The shelter blocks the heat only.
        var moon = ProfileOfBody(1);
        var open = Live(moon, Day, sheltered: false, generator: false, lifeSupport: false, _full, 10).Suit;
        Assert.Equal(97f, open.Cooling, 2);
        Assert.Equal(97f, open.Shield, 2);

        var sheltered = Live(moon, Day, sheltered: true, generator: false, lifeSupport: false, _full, 10).Suit;

        Assert.Equal(100f, sheltered.Cooling, 2);
        Assert.Equal(97f, sheltered.Shield, 2);
        Assert.Equal(95f, sheltered.Oxygen, 2);
    }

    [Fact]
    public void Shelter_RegeneratesHeating()
    {
        // The home planet by night, the heating at 50: in the open it keeps falling, under a roof the cold is kept out and the heating regenerates at 5 a second, 100 after 10 s.
        var home = ProfileOfBody(0);
        var start = _full with { Heating = 50f };
        Assert.True(Live(home, Night, sheltered: false, generator: false, lifeSupport: false, start, 10).Suit.Heating < 50f);

        var (suit, damage) = Live(home, Night, sheltered: true, generator: false, lifeSupport: false, start, 10);

        Assert.Equal(100f, suit.Heating, 2);
        Assert.Equal(0f, damage);
    }

    [Fact]
    public void Generator_ProtectsVacuumAndRegeneratesOxygen()
    {
        // The moon by night under a roof and by a running generator: the cold and the vacuum are both kept out, nothing drains and the oxygen regenerates from 40 to 100 in 12 s (13 s are simulated).
        var moon = ProfileOfBody(1);
        Assert.True(NextToARunningGenerator());
        var start = _full with { Oxygen = 40f };

        var (suit, damage) = Live(moon, Night, sheltered: true, generator: true, lifeSupport: false, start, 13);

        Assert.Equal(100f, suit.Oxygen, 2);
        Assert.Equal(0f, damage);

        // Without the generator the vacuum goes on and the oxygen does not regenerate.
        var without = Live(moon, Night, sheltered: true, generator: false, lifeSupport: false, start, 12).Suit;
        Assert.Equal(40f - 6f, without.Oxygen, 2);
    }

    [Fact]
    public void ShipSeat_FullLifeSupport()
    {
        // Space: cold 0.3, radiation 0.3 and vacuum 0.5 day and night. In the seat of a ship the life support protects from everything and the suit fills up.
        var state = StarWorld();
        var space = HazardProfileResolver.Resolve(state, HazardTestContent.Hazards, null, 0f);
        Assert.Equal(HazardTestContent.SpaceKey, space.Key);
        var start = new SuitResources(10f, 100f, 100f, 20f, 30f);

        var (suit, damage) = Live(space, Day, sheltered: false, generator: false, lifeSupport: true, start, 20);

        Assert.Equal(_full, suit);
        Assert.Equal(0f, damage);

        // The twin out of the seat: the same minutes drain the oxygen, the shield and the heating.
        var outside = Live(space, Day, sheltered: false, generator: false, lifeSupport: false, start, 20).Suit;
        Assert.Equal(20f, outside.Oxygen, 2);
        Assert.Equal(14f, outside.Shield, 2);
        Assert.Equal(4f, outside.Heating, 2);
    }

    [Fact]
    public void MoonVacuum_AtZeroOxygen_DamageTenPerSecond()
    {
        // The acceptance line of the spec (line 135): at zero oxygen the vacuum takes 10 health a second. The oxygen of 0.5 lasts 200 s from 100; from 1 it lasts 2 s and the damage comes after.
        var moon = ProfileOfBody(1);
        var start = _full with { Oxygen = 1f };

        var (suit, damage) = Live(moon, Night, sheltered: true, generator: false, lifeSupport: false, start, 12);

        Assert.Equal(0f, suit.Oxygen, 2);
        Assert.Equal(100f, suit.Heating, 2);
        Assert.Equal(100f, damage, 0);
    }
}
