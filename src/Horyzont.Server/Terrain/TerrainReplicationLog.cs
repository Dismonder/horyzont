using Horyzont.Core.World.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Terrain;

/// <summary>Log entries of the terrain replication of the server.</summary>
internal static partial class TerrainReplicationLog
{
    [LoggerMessage(EventId = 9801, Level = LogLevel.Warning, Message = "Player {Player}: terrain viewer report ignored: {Reason}")]
    public static partial void ViewRejected(this ILogger logger, ulong player, string reason);

    [LoggerMessage(EventId = 9802, Level = LogLevel.Warning, Message = "Player {Player}: cache report of planet {Planet} exceeds {Limit} keys; the rest is ignored")]
    public static partial void ReportTruncated(this ILogger logger, ulong player, ulong planet, int limit);

    [LoggerMessage(EventId = 9803, Level = LogLevel.Warning, Message = "Player {Player}: generator mismatch at {Key} (client digest {ClientDigest}, build {Build}); the server block is sent (count {Count})")]
    public static partial void GeneratorMismatch(this ILogger logger, ulong player, TerrainBlockKey key, ulong clientDigest, string build, long count);

    [LoggerMessage(EventId = 9804, Level = LogLevel.Error, Message = "Edited terrain block {Key} has no data to send")]
    public static partial void ExportUnavailable(this ILogger logger, TerrainBlockKey key);

    [LoggerMessage(EventId = 9805, Level = LogLevel.Debug, Message = "Player {Player}: checksum message ignored: {Reason}")]
    public static partial void ChecksumIgnored(this ILogger logger, ulong player, string reason);

    [LoggerMessage(EventId = 9806, Level = LogLevel.Warning, Message = "Player {Player}: terrain send queue overflowed; the interest is computed again")]
    public static partial void QueueOverflow(this ILogger logger, ulong player);
}
