using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Survival;

/// <summary>Log entries of the vitals service (K6-33); each names the player.</summary>
internal static partial class VitalsLog
{
    [LoggerMessage(EventId = 8601, Level = LogLevel.Warning, Message = "The vitals of player {PlayerId} were refused by the simulation")]
    public static partial void VitalsRefused(this ILogger logger, ulong playerId);

    [LoggerMessage(EventId = 8602, Level = LogLevel.Debug, Message = "Player {PlayerId} lies down in a bed of construct {ConstructId}")]
    public static partial void LyingStarted(this ILogger logger, ulong playerId, ulong constructId);

    [LoggerMessage(EventId = 8603, Level = LogLevel.Debug, Message = "Player {PlayerId} took {Damage} damage from a fall at {Speed} m/s")]
    public static partial void FallDamage(this ILogger logger, ulong playerId, float damage, float speed);

    [LoggerMessage(EventId = 8604, Level = LogLevel.Warning, Message = "The hazard intensity {Intensity} of the world rules is outside 0.5 to 2 or not a number; {Used} is used")]
    public static partial void HazardIntensityAdjusted(this ILogger logger, float intensity, float used);

    [LoggerMessage(EventId = 8605, Level = LogLevel.Debug, Message = "Player {PlayerId} is hurt by a hazard of the planet (mask {ExposedMask}): a resource of the suit ran out")]
    public static partial void SuitExposed(this ILogger logger, ulong playerId, byte exposedMask);
}
