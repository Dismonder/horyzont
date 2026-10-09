using Horyzont.Core.World.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence.Terrain;

/// <summary>Log messages of the terrain block buffer.</summary>
internal static partial class TerrainLog
{
    [LoggerMessage(EventId = 130, Level = LogLevel.Error, Message = "Terrain block {Key} has version {Version} but no saved data; the block cannot be loaded")]
    internal static partial void BlockWithoutData(ILogger logger, TerrainBlockKey key, uint version);

    [LoggerMessage(EventId = 131, Level = LogLevel.Error, Message = "Reading terrain block {Key} failed; the block cannot be loaded")]
    internal static partial void ReadFailed(ILogger logger, Exception exception, TerrainBlockKey key);

    [LoggerMessage(EventId = 132, Level = LogLevel.Warning, Message = "Terrain buffer saturated with {Pending} blocks waiting for a checkpoint; terrain edits are refused until one commits")]
    internal static partial void Saturated(ILogger logger, int pending);

    [LoggerMessage(EventId = 133, Level = LogLevel.Information, Message = "Terrain buffer below its limit again ({Pending} blocks waiting)")]
    internal static partial void SaturationCleared(ILogger logger, int pending);

    [LoggerMessage(EventId = 134, Level = LogLevel.Warning, Message = "Checkpoint at barrier {OpId} failed; its {Count} terrain blocks stay in the buffer")]
    internal static partial void CheckpointFailedKept(ILogger logger, ulong opId, int count);

    [LoggerMessage(EventId = 135, Level = LogLevel.Warning, Message = "Terrain buffer closed while {Reads} block reads were still running after {TimeoutMs} ms")]
    internal static partial void CloseTimedOut(ILogger logger, int reads, int timeoutMs);

    [LoggerMessage(EventId = 136, Level = LogLevel.Error, Message = "Terrain block {Key} changed version during each of {Attempts} read attempts; the block cannot be loaded now")]
    internal static partial void LoadRetriesExhausted(ILogger logger, TerrainBlockKey key, int attempts);
}
