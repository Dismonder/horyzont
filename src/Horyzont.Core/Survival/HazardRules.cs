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
    public float SuitBaseCapacity { get; init; } = 100f;

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
}
