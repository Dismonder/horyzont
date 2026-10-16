using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Props;

/// <summary>Log entries of the prop systems (regrowth and replication); each names the prop, the planet or the player.</summary>
internal static partial class PropsLog
{
    [LoggerMessage(EventId = 8501, Level = LogLevel.Warning, Message = "Harvested prop {PropId} of planet {PlanetId} does not exist in the layout of the planet any more; it stays harvested")]
    public static partial void UnknownHarvestedProp(this ILogger logger, ulong propId, ulong planetId);

    [LoggerMessage(EventId = 8502, Level = LogLevel.Debug, Message = "Regrew {Count} props in operation {OpId}")]
    public static partial void PropsRegrown(this ILogger logger, int count, ulong opId);

    [LoggerMessage(EventId = 8503, Level = LogLevel.Error, Message = "The regrowth of {Count} props was refused by the simulation; the scan goes on")]
    public static partial void RegrowFailed(this ILogger logger, int count, Exception exception);

    [LoggerMessage(EventId = 8511, Level = LogLevel.Debug, Message = "Sent the states of {Regions} prop regions of planet {PlanetId} to player {PlayerId}")]
    public static partial void RegionsSent(this ILogger logger, int regions, ulong planetId, ulong playerId);

    [LoggerMessage(EventId = 8512, Level = LogLevel.Debug, Message = "Sent the states of {Regions} prop regions changed by operation {OpId} to {Players} players")]
    public static partial void RegionsChanged(this ILogger logger, int regions, ulong opId, int players);
}
