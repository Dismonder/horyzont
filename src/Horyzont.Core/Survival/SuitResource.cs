namespace Horyzont.Core.Survival;

/// <summary>A resource of the suit (K6-34, D1, D4); the byte is the one of operation 42.</summary>
public enum SuitResource : byte
{
    /// <summary>Heating, drained by cold.</summary>
    Heating = 1,

    /// <summary>Cooling, drained by heat.</summary>
    Cooling = 2,

    /// <summary>Filter, drained by toxins.</summary>
    Filter = 3,

    /// <summary>Shield, drained by radiation.</summary>
    Shield = 4,

    /// <summary>Oxygen, drained by vacuum.</summary>
    Oxygen = 5,
}
