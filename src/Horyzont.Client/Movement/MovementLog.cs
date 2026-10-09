using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Movement;

/// <summary>Log entries of client movement prediction.</summary>
internal static partial class MovementLog
{
    [LoggerMessage(EventId = 9101, Level = LogLevel.Debug, Message = "Prediction corrected at input {Seq}: position error {PositionError} m, velocity error {VelocityError} m/s, {Replayed} inputs to replay")]
    public static partial void Corrected(this ILogger logger, uint seq, float positionError, float velocityError, int replayed);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Warning, Message = "Prediction paused: {Unacknowledged} inputs are not acknowledged by the server")]
    public static partial void PredictionPaused(this ILogger logger, int unacknowledged);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Warning, Message = "Own snapshot {SnapshotId} rejected: {Reason} ({Rejected} rejected so far)")]
    public static partial void SnapshotRejected(this ILogger logger, uint snapshotId, OwnStateVerdict reason, int rejected);

    [LoggerMessage(EventId = 9104, Level = LogLevel.Warning, Message = "Unknown motion flags {Flags} in snapshot {SnapshotId}, treated as falling")]
    public static partial void UnknownMotionFlags(this ILogger logger, byte flags, uint snapshotId);
}
