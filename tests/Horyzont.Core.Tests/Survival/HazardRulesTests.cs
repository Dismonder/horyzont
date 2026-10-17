using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Core.Tests.Survival;

/// <summary>The numbers of <see cref="HazardRules.Default"/> (K6-34 D10, D11) and the validation of their ranges (review B1, P3-7).</summary>
public sealed class HazardRulesTests
{
    [Fact]
    public void Defaults_OfK6_34()
    {
        var rules = HazardRules.Default;

        // Spec 03 lines 129 to 140: 2/2/2/3/10 health a second at zero, 26 rays of 16 m with 21 hits for the shelter, the generator within 10 m.
        Assert.Equal(2f, rules.ZeroDamageCold);
        Assert.Equal(2f, rules.ZeroDamageHeat);
        Assert.Equal(2f, rules.ZeroDamageToxins);
        Assert.Equal(3f, rules.ZeroDamageRadiation);
        Assert.Equal(10f, rules.ZeroDamageVacuum);
        Assert.Equal(16f, rules.ShelterRayMeters);
        Assert.Equal(21, rules.ShelterRequiredHits);
        Assert.Equal(10f, rules.GeneratorRangeMeters);

        // The tuning of the package (D10, DO USTALENIA-6): data, changed here together with the plan.
        Assert.Equal((ushort)100, rules.SuitBaseCapacity);
        Assert.Equal(5f, rules.SuitRegenPerSecond);
        Assert.Equal(0.25f, rules.MinDrainMultiplier);
        Assert.Equal(1f, rules.ShelterOriginHeightMeters);
        Assert.Equal(10, rules.HazardEvaluateEveryTicks);
        Assert.Equal(0.1f, rules.TwilightBand);
        Assert.Equal(ShelterDirections.Count, 26);
        Assert.True(rules.SuitBaseCapacity <= ProtocolConstants.MaxSuitValue);
    }

    [Fact]
    public void Defaults_AreValid()
    {
        HazardRules.Default.Validate();
    }

    [Fact]
    public void ZeroDamage_MatchesTheProperties()
    {
        var rules = HazardRules.Default;

        Assert.Equal(rules.ZeroDamageCold, rules.ZeroDamage(HazardKind.Cold));
        Assert.Equal(rules.ZeroDamageHeat, rules.ZeroDamage(HazardKind.Heat));
        Assert.Equal(rules.ZeroDamageToxins, rules.ZeroDamage(HazardKind.Toxins));
        Assert.Equal(rules.ZeroDamageRadiation, rules.ZeroDamage(HazardKind.Radiation));
        Assert.Equal(rules.ZeroDamageVacuum, rules.ZeroDamage(HazardKind.Vacuum));
    }

    public static TheoryData<string, HazardRules> Invalid() => new()
    {
        { nameof(HazardRules.SuitBaseCapacity), HazardRules.Default with { SuitBaseCapacity = 0 } },
        { nameof(HazardRules.SuitBaseCapacity), HazardRules.Default with { SuitBaseCapacity = 1001 } },
        { nameof(HazardRules.SuitRegenPerSecond), HazardRules.Default with { SuitRegenPerSecond = -1f } },
        { nameof(HazardRules.SuitRegenPerSecond), HazardRules.Default with { SuitRegenPerSecond = float.NaN } },
        { nameof(HazardRules.MinDrainMultiplier), HazardRules.Default with { MinDrainMultiplier = 0f } },
        { nameof(HazardRules.MinDrainMultiplier), HazardRules.Default with { MinDrainMultiplier = 1.01f } },
        { nameof(HazardRules.MinDrainMultiplier), HazardRules.Default with { MinDrainMultiplier = float.NaN } },
        { nameof(HazardRules.ZeroDamageCold), HazardRules.Default with { ZeroDamageCold = -0.5f } },
        { nameof(HazardRules.ZeroDamageHeat), HazardRules.Default with { ZeroDamageHeat = float.PositiveInfinity } },
        { nameof(HazardRules.ZeroDamageToxins), HazardRules.Default with { ZeroDamageToxins = float.NaN } },
        { nameof(HazardRules.ZeroDamageRadiation), HazardRules.Default with { ZeroDamageRadiation = -1f } },
        { nameof(HazardRules.ZeroDamageVacuum), HazardRules.Default with { ZeroDamageVacuum = float.NegativeInfinity } },
        { nameof(HazardRules.ShelterRayMeters), HazardRules.Default with { ShelterRayMeters = -1f } },
        { nameof(HazardRules.ShelterRayMeters), HazardRules.Default with { ShelterRayMeters = float.NaN } },
        { nameof(HazardRules.ShelterRequiredHits), HazardRules.Default with { ShelterRequiredHits = 0 } },
        { nameof(HazardRules.ShelterRequiredHits), HazardRules.Default with { ShelterRequiredHits = 27 } },
        { nameof(HazardRules.ShelterOriginHeightMeters), HazardRules.Default with { ShelterOriginHeightMeters = -0.1f } },
        { nameof(HazardRules.GeneratorRangeMeters), HazardRules.Default with { GeneratorRangeMeters = -10f } },
        { nameof(HazardRules.GeneratorRangeMeters), HazardRules.Default with { GeneratorRangeMeters = float.PositiveInfinity } },
        { nameof(HazardRules.HazardEvaluateEveryTicks), HazardRules.Default with { HazardEvaluateEveryTicks = 0 } },
        { nameof(HazardRules.HazardEvaluateEveryTicks), HazardRules.Default with { HazardEvaluateEveryTicks = -3 } },
        { nameof(HazardRules.TwilightBand), HazardRules.Default with { TwilightBand = 0f } },
        { nameof(HazardRules.TwilightBand), HazardRules.Default with { TwilightBand = -0.1f } },
        { nameof(HazardRules.TwilightBand), HazardRules.Default with { TwilightBand = float.NaN } },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_Throws(string property, HazardRules rules)
    {
        HazardRules.Default.Validate();

        var e = Assert.Throws<ArgumentOutOfRangeException>(rules.Validate);
        Assert.Equal(property, e.ParamName);
    }

    [Theory]
    [InlineData(1, 1f, 1, 1)]
    [InlineData(1000, 0.01f, 26, 1)]
    public void Boundaries_Accepted(int capacity, float drain, int hits, int ticks)
    {
        (HazardRules.Default with
        {
            SuitBaseCapacity = (ushort)capacity,
            MinDrainMultiplier = drain,
            ShelterRequiredHits = hits,
            HazardEvaluateEveryTicks = ticks,
            ShelterRayMeters = 0f,
            GeneratorRangeMeters = 0f,
            SuitRegenPerSecond = 0f,
        }).Validate();
    }

    [Fact]
    public void ServerSimulation_InvalidHazardRules_Throws()
    {
        var state = WorldFactory.CreateDefault();
        var good = HazardRules.Default with { SuitBaseCapacity = 80 };

        // A broken configuration fails at the start, not at the first refill (a base capacity of 0 would make every operation 42 of the server malformed).
        var sim = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance, Horyzont.Core.World.Blocks.BlockContentTable.Empty, null, null, good);
        Assert.Equal(good, sim.HazardRules);
        Assert.Same(HazardRules.Default, new ServerSimulation(WorldFactory.CreateDefault(), new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance).HazardRules);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerSimulation(
            WorldFactory.CreateDefault(), new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance,
            Horyzont.Core.World.Blocks.BlockContentTable.Empty, null, null, HazardRules.Default with { SuitBaseCapacity = 0 }));
    }

    [Fact]
    public void DefaultSuit_EqualsTheBaseCapacity()
    {
        // The suit of a server without hazard content (message 44) and the base capacity of the rules are two hundreds that must not drift apart (review B1b, P3-9).
        var expected = (float)HazardRules.Default.SuitBaseCapacity;
        var suit = Horyzont.Core.Protocol.Messages.PlayerVitalsMessage.DefaultSuit;

        foreach (var resource in Enum.GetValues<SuitResource>())
        {
            Assert.Equal(expected, suit[resource]);
        }
    }
}
