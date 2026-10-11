using Horyzont.Core.Ids;
using Horyzont.Server.Frames;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Space;

/// <summary>Log messages of the star system on the server (K5-21).</summary>
internal static partial class SpaceLog
{
    [LoggerMessage(EventId = 8401, Level = LogLevel.Information, Message = "Ship {Ship} crossed a zone border into {Kind}:{Frame}")]
    internal static partial void ShipReparented(ILogger logger, ulong ship, FrameKind kind, ulong frame);

    [LoggerMessage(EventId = 8402, Level = LogLevel.Warning, Message = "Ship {Ship} could not cross a zone border into {Kind}:{Frame}: {Failure}; retrying every tick")]
    internal static partial void ZoneTransitionRefused(ILogger logger, ulong ship, FrameKind kind, ulong frame, FrameTransitionFailure failure);
}
