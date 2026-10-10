using Horyzont.Core.World.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client.Terrain;

/// <summary>Log entries of the terrain replication of a remote client.</summary>
internal static partial class ClientTerrainLog
{
    [LoggerMessage(EventId = 9901, Level = LogLevel.Warning, Message = "Terrain block {Key} is not a valid envelope")]
    internal static partial void BadBlock(ILogger logger, Exception exception, TerrainBlockKey key);

    [LoggerMessage(EventId = 9902, Level = LogLevel.Error, Message = "Leaving the server: terrain replication broke the protocol: {Reason}")]
    internal static partial void Leaving(ILogger logger, string reason);

    [LoggerMessage(EventId = 9903, Level = LogLevel.Warning, Message = "The terrain cache could not be drained in time; its file stays open until the process ends")]
    internal static partial void CacheCloseTimedOut(ILogger logger);

    [LoggerMessage(EventId = 9904, Level = LogLevel.Error, Message = "Closing the terrain cache failed")]
    internal static partial void CacheCloseFailed(ILogger logger, Exception exception);
}
