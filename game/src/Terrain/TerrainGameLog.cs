using Horyzont.Core.World.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Terrain;

/// <summary>Log messages of the terrain adapter.</summary>
internal static partial class TerrainGameLog
{
    [LoggerMessage(EventId = 140, Level = LogLevel.Error, Message = "Terrain block {Key} is unavailable; Voxel Tools gets a load error and the server stops accepting terrain edits")]
    internal static partial void BlockUnavailable(ILogger logger, TerrainBlockKey key);

    [LoggerMessage(EventId = 141, Level = LogLevel.Error, Message = "Terrain block {Key} could not be decoded; Voxel Tools gets a load error and the server stops accepting terrain edits")]
    internal static partial void BlockCorrupt(ILogger logger, Exception exception, TerrainBlockKey key);

    [LoggerMessage(EventId = 142, Level = LogLevel.Error, Message = "Terrain stream call for block {Key} failed; Voxel Tools gets an error")]
    internal static partial void StreamCallFailed(ILogger logger, Exception exception, TerrainBlockKey key);

    [LoggerMessage(EventId = 143, Level = LogLevel.Error, Message = "Voxel Tools saved LOD 0 block {Key} with data that differs from the terrain buffer")]
    internal static partial void SavedBlockDiffers(ILogger logger, TerrainBlockKey key);

    [LoggerMessage(EventId = 144, Level = LogLevel.Error, Message = "The terrain stream of planet {Planet} reported an error at block {Key}; terrain edits on it are refused")]
    internal static partial void StreamFaultObserved(ILogger logger, ulong planet, TerrainBlockKey key);

    [LoggerMessage(EventId = 145, Level = LogLevel.Information, Message = "Terrain service shut down; block reads finished: {Drained}")]
    internal static partial void ShutDown(ILogger logger, bool drained);
}
