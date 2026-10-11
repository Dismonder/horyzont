using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Server.Frames;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Server.Characters;

/// <summary>Log messages of the server characters (events 9301 and up).</summary>
internal static partial class ServerCharactersLog
{
    [LoggerMessage(EventId = 9301, Level = LogLevel.Information, Message = "Character of player {Player} spawned at {X:F1} {Y:F1} {Z:F1}")]
    internal static partial void Spawned(this ILogger logger, ulong player, float x, float y, float z);

    [LoggerMessage(EventId = 9302, Level = LogLevel.Information, Message = "Character of player {Player} removed")]
    internal static partial void Despawned(this ILogger logger, ulong player);

    [LoggerMessage(EventId = 9303, Level = LogLevel.Warning, Message = "Player {Player} cannot get a character: {Reason}")]
    internal static partial void SpawnRefused(this ILogger logger, ulong player, string reason);

    [LoggerMessage(EventId = 9304, Level = LogLevel.Warning, Message = "Player {Player} sent an invalid input (seq {Seq}); a neutral input is used ({Rejected} rejected so far)")]
    internal static partial void InvalidInput(this ILogger logger, ulong player, uint seq, int rejected);

    [LoggerMessage(EventId = 9305, Level = LogLevel.Debug, Message = "Player {Player} sent no input for {Ticks} ticks")]
    internal static partial void InputStarved(this ILogger logger, ulong player, int ticks);

    [LoggerMessage(EventId = 9306, Level = LogLevel.Warning, Message = "Terrain collision under player {Player} did not appear within {Ticks} ticks; the character is let go")]
    internal static partial void GroundWaitExpired(this ILogger logger, ulong player, int ticks);

    [LoggerMessage(EventId = 9307, Level = LogLevel.Warning, Message = "Player {Player} fell below the core ({Radius:F1} m from the centre); put back above the surface")]
    internal static partial void RescuedFromCore(this ILogger logger, ulong player, float radius);

    [LoggerMessage(EventId = 9308, Level = LogLevel.Warning, Message = "The simulation refused the kinematics of player {Player}; the body returns to the simulation pose ({Rejected} so far)")]
    internal static partial void KinematicsRefused(this ILogger logger, ulong player, int rejected);

    [LoggerMessage(EventId = 9309, Level = LogLevel.Debug, Message = "Character of player {Player} stands on the ground after {Ticks} ticks, mode {Mode}")]
    internal static partial void Grounded(this ILogger logger, ulong player, int ticks, MovementMode mode);

    [LoggerMessage(EventId = 9310, Level = LogLevel.Information, Message = "Character of player {Player} follows a reparent to {Kind} {Frame} (seq {Seq}): {Action}")]
    internal static partial void FrameChanged(this ILogger logger, ulong player, FrameKind kind, ulong frame, uint seq, CharacterFrameAction action);
}
