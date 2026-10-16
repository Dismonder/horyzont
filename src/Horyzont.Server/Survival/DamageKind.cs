namespace Horyzont.Server.Survival;

/// <summary>
/// What hurt a player (K6-33 D13). Lives in memory only, not in any format: later packages (hazards K6-34, combat K7-42) append their kinds with the next numbers.
/// </summary>
public enum DamageKind : byte
{
    /// <summary>A fall: the speed of the landing above the safe speed.</summary>
    Fall = 1,

    /// <summary>No heating in the suit while the cold is active (K6-34).</summary>
    Cold = 2,

    /// <summary>No cooling in the suit while the heat is active (K6-34).</summary>
    Heat = 3,

    /// <summary>No filter in the suit while the toxins are active (K6-34).</summary>
    Toxins = 4,

    /// <summary>No shield in the suit while the radiation is active (K6-34).</summary>
    Radiation = 5,

    /// <summary>No oxygen in the suit while there is a vacuum (K6-34).</summary>
    Vacuum = 6,
}
