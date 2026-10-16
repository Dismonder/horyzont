namespace Horyzont.Core.Survival;

/// <summary>A hazard of a planet or of space (K6-34, D1, D10). The value is also the value of the <see cref="SuitResource"/> that the hazard drains.</summary>
public enum HazardKind : byte
{
    /// <summary>Cold: drains the heating.</summary>
    Cold = 1,

    /// <summary>Heat: drains the cooling.</summary>
    Heat = 2,

    /// <summary>Toxins: drain the filter.</summary>
    Toxins = 3,

    /// <summary>Radiation: drains the shield.</summary>
    Radiation = 4,

    /// <summary>Vacuum: drains the oxygen.</summary>
    Vacuum = 5,
}
