using Horyzont.Core.Protocol;

namespace Horyzont.Core.Survival;

/// <summary>The five resources of the suit (K6-34, D4): each 0 to <see cref="ProtocolConstants.MaxSuitValue"/>; the capacity of a resource is 100 plus the modules.</summary>
/// <param name="Heating">Heating.</param>
/// <param name="Cooling">Cooling.</param>
/// <param name="Filter">Filter.</param>
/// <param name="Shield">Shield.</param>
/// <param name="Oxygen">Oxygen.</param>
public readonly record struct SuitResources(float Heating, float Cooling, float Filter, float Shield, float Oxygen)
{
    /// <summary>Gets the value of a resource.</summary>
    /// <param name="resource">The resource, 1 to 5.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resource"/> is not a resource.</exception>
    public float this[SuitResource resource] => resource switch
    {
        SuitResource.Heating => Heating,
        SuitResource.Cooling => Cooling,
        SuitResource.Filter => Filter,
        SuitResource.Shield => Shield,
        SuitResource.Oxygen => Oxygen,
        _ => throw new ArgumentOutOfRangeException(nameof(resource)),
    };

    /// <summary>Gets a value indicating whether every value is finite and within 0 to <see cref="ProtocolConstants.MaxSuitValue"/>.</summary>
    public bool IsValid => Ok(Heating) && Ok(Cooling) && Ok(Filter) && Ok(Shield) && Ok(Oxygen);

    /// <summary>Replaces one value.</summary>
    /// <param name="resource">The resource, 1 to 5.</param>
    /// <param name="value">The new value.</param>
    /// <returns>The resources with the value replaced.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resource"/> is not a resource.</exception>
    public SuitResources With(SuitResource resource, float value) => resource switch
    {
        SuitResource.Heating => this with { Heating = value },
        SuitResource.Cooling => this with { Cooling = value },
        SuitResource.Filter => this with { Filter = value },
        SuitResource.Shield => this with { Shield = value },
        SuitResource.Oxygen => this with { Oxygen = value },
        _ => throw new ArgumentOutOfRangeException(nameof(resource)),
    };

    private static bool Ok(float value) => float.IsFinite(value) && value >= 0f && value <= ProtocolConstants.MaxSuitValue;
}
