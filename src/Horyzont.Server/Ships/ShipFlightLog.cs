using Horyzont.Server.Space;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Ships;

/// <summary>Log messages of the flight of ships (K5-24).</summary>
internal static partial class ShipFlightLog
{
    [LoggerMessage(EventId = 2401, Level = LogLevel.Warning,
        Message = "Terrain unavailable for ship {Ship} on planet {Planet}: readiness {Readiness} after {WaitedSeconds:F1} s; the ship holds and retries")]
    internal static partial void TerrainUnavailable(this ILogger logger, ulong ship, ulong planet, PlanetTerrainReadiness readiness, double waitedSeconds);

    [LoggerMessage(EventId = 2402, Level = LogLevel.Information, Message = "Ship {Ship} settled after load, {Drop:F1} m down")]
    internal static partial void ShipSettled(this ILogger logger, ulong ship, double drop);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Warning, Message = "Ship {Ship} pose cannot be stored in frame {Frame}; the ship stops")]
    internal static partial void PoseOutOfRange(this ILogger logger, ulong ship, string frame);
}
