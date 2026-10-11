using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Ships;

/// <summary>Log messages of seats and piloting (K5-25).</summary>
internal static partial class SeatLog
{
    [LoggerMessage(EventId = 2501, Level = LogLevel.Information,
        Message = "Pilot inputs of player {Player} dropped: not the pilot of any ship (NotPilot, {Count} so far)")]
    internal static partial void NotPilotDropped(this ILogger logger, ulong player, long count);

    [LoggerMessage(EventId = 2502, Level = LogLevel.Information,
        Message = "Pilot slots of ship {Ship} re-anchored for player {Player}: next slot {NextSeq} ({Reanchors} so far)")]
    internal static partial void Reanchored(this ILogger logger, ulong ship, ulong player, uint nextSeq, long reanchors);

    [LoggerMessage(EventId = 2503, Level = LogLevel.Information,
        Message = "Ship {Ship} launch guard: seated player {Player} on {X} {Y} {Z}")]
    internal static partial void AutoSeated(this ILogger logger, ulong ship, ulong player, int x, int y, int z);

    [LoggerMessage(EventId = 2504, Level = LogLevel.Information, Message = "Ship {Ship} launch blocked: person in hull")]
    internal static partial void PersonInHull(this ILogger logger, ulong ship);
}
