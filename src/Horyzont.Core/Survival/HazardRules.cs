using Horyzont.Core.Protocol;

namespace Horyzont.Core.Survival;

/// <summary>
/// Constants of the hazards and the suit (K6-34, D10, D11). The suit is outside the journal like the vitals, so the values are plain <c>init</c> properties like
/// <see cref="VitalsRules"/>; client and server use the same ones because the build hash in the handshake forces the same build.
/// </summary>
public sealed record HazardRules
{
    /// <summary>Gets the default rules.</summary>
    public static HazardRules Default { get; } = new();

    /// <summary>Capacity of every resource without modules.</summary>
    public ushort SuitBaseCapacity { get; init; } = 100;

    /// <summary>Regeneration of a resource per second while it is protected and allowed to regenerate.</summary>
    public float SuitRegenPerSecond { get; init; } = 5f;

    /// <summary>The lowest drain multiplier the modules can reach.</summary>
    public float MinDrainMultiplier { get; init; } = 0.25f;

    /// <summary>Health lost per second at zero heating while cold is active.</summary>
    public float ZeroDamageCold { get; init; } = 2f;

    /// <summary>Health lost per second at zero cooling while heat is active.</summary>
    public float ZeroDamageHeat { get; init; } = 2f;

    /// <summary>Health lost per second at zero filter while toxins are active.</summary>
    public float ZeroDamageToxins { get; init; } = 2f;

    /// <summary>Health lost per second at zero shield while radiation is active.</summary>
    public float ZeroDamageRadiation { get; init; } = 3f;

    /// <summary>Health lost per second at zero oxygen while vacuum is active.</summary>
    public float ZeroDamageVacuum { get; init; } = 10f;

    /// <summary>Length of a shelter ray in metres.</summary>
    public float ShelterRayMeters { get; init; } = 16f;

    /// <summary>Rays of the 26 that must hit a block or the terrain, the ray upwards among them, for a shelter.</summary>
    public int ShelterRequiredHits { get; init; } = 21;

    /// <summary>Height of the origin of the shelter rays above the feet.</summary>
    public float ShelterOriginHeightMeters { get; init; } = 1f;

    /// <summary>Range of an oxygen generator in metres.</summary>
    public float GeneratorRangeMeters { get; init; } = 10f;

    /// <summary>Ticks between two evaluations of the surroundings of a player.</summary>
    public int HazardEvaluateEveryTicks { get; init; } = 10;

    /// <summary>Half width of the twilight band of the day-night blend, in the dot product of the up direction and the sun direction.</summary>
    public float TwilightBand { get; init; } = 0.1f;

    /// <summary>
    /// Checks the ranges the rest of the code relies on (K6-34 review B1, P3-7): a base capacity of 0 would make every refill of the server a malformed operation 42, a period of 0 ticks divides by zero in the
    /// tracker, and a number of hits above 26 could never be reached. <see cref="Simulation.ServerSimulation"/> calls it once, at its construction.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its range; the message names the property.</exception>
    public void Validate()
    {
        Require(SuitBaseCapacity is >= 1 and <= (ushort)ProtocolConstants.MaxSuitValue, SuitBaseCapacity, nameof(SuitBaseCapacity));
        Require(IsNonNegative(SuitRegenPerSecond), SuitRegenPerSecond, nameof(SuitRegenPerSecond));
        Require(MinDrainMultiplier is > 0f and <= 1f, MinDrainMultiplier, nameof(MinDrainMultiplier));
        Require(IsNonNegative(ZeroDamageCold), ZeroDamageCold, nameof(ZeroDamageCold));
        Require(IsNonNegative(ZeroDamageHeat), ZeroDamageHeat, nameof(ZeroDamageHeat));
        Require(IsNonNegative(ZeroDamageToxins), ZeroDamageToxins, nameof(ZeroDamageToxins));
        Require(IsNonNegative(ZeroDamageRadiation), ZeroDamageRadiation, nameof(ZeroDamageRadiation));
        Require(IsNonNegative(ZeroDamageVacuum), ZeroDamageVacuum, nameof(ZeroDamageVacuum));
        Require(IsNonNegative(ShelterRayMeters), ShelterRayMeters, nameof(ShelterRayMeters));
        Require(ShelterRequiredHits is >= 1 and <= ShelterDirections.Count, ShelterRequiredHits, nameof(ShelterRequiredHits));
        Require(IsNonNegative(ShelterOriginHeightMeters), ShelterOriginHeightMeters, nameof(ShelterOriginHeightMeters));
        Require(IsNonNegative(GeneratorRangeMeters), GeneratorRangeMeters, nameof(GeneratorRangeMeters));
        Require(HazardEvaluateEveryTicks >= 1, HazardEvaluateEveryTicks, nameof(HazardEvaluateEveryTicks));
        Require(float.IsFinite(TwilightBand) && TwilightBand > 0f, TwilightBand, nameof(TwilightBand));
    }

    /// <summary>Gets the health lost per second at zero for the resource of a hazard.</summary>
    /// <param name="kind">The hazard, 1 to 5.</param>
    /// <returns>The damage per second.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a hazard.</exception>
    public float ZeroDamage(HazardKind kind) => kind switch
    {
        HazardKind.Cold => ZeroDamageCold,
        HazardKind.Heat => ZeroDamageHeat,
        HazardKind.Toxins => ZeroDamageToxins,
        HazardKind.Radiation => ZeroDamageRadiation,
        HazardKind.Vacuum => ZeroDamageVacuum,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool IsNonNegative(float value) => float.IsFinite(value) && value >= 0f;

    private static void Require(bool valid, object value, string name)
    {
        if (!valid)
        {
            throw new ArgumentOutOfRangeException(name, value, $"HazardRules.{name} is out of range.");
        }
    }
}
