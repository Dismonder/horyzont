namespace Horyzont.Core.Survival;

/// <summary>A rate per hazard (K6-34, D10): points of the matching suit resource per second, or a factor; the five values are indexed by <see cref="HazardKind"/>.</summary>
/// <param name="Cold">Cold.</param>
/// <param name="Heat">Heat.</param>
/// <param name="Toxins">Toxins.</param>
/// <param name="Radiation">Radiation.</param>
/// <param name="Vacuum">Vacuum.</param>
public readonly record struct HazardRates(float Cold, float Heat, float Toxins, float Radiation, float Vacuum)
{
    /// <summary>Gets the value of a hazard.</summary>
    /// <param name="kind">The hazard, 1 to 5.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a hazard.</exception>
    public float this[HazardKind kind] => kind switch
    {
        HazardKind.Cold => Cold,
        HazardKind.Heat => Heat,
        HazardKind.Toxins => Toxins,
        HazardKind.Radiation => Radiation,
        HazardKind.Vacuum => Vacuum,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
