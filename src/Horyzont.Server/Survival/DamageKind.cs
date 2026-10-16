namespace Horyzont.Server.Survival;

/// <summary>
/// What hurt a player (K6-33 D13). Lives in memory only, not in any format: later packages (hazards K6-34, combat K7-42) append their kinds with the next numbers.
/// </summary>
public enum DamageKind : byte
{
    /// <summary>A fall: the speed of the landing above the safe speed.</summary>
    Fall = 1,
}
