namespace Horyzont.Server.Frames;

/// <summary>Why <c>FrameTransitions.TryReparent</c> did not reparent an entity.</summary>
public enum FrameTransitionFailure : byte
{
    /// <summary>Reparented.</summary>
    None = 0,

    /// <summary>The entity is neither a player nor a construct.</summary>
    UnknownEntity = 1,

    /// <summary>The new frame does not exist.</summary>
    UnknownFrame = 2,

    /// <summary>A player cannot enter the system frame in EA (P6).</summary>
    PlayerToSystem = 3,

    /// <summary>Only ship-mode constructs change frames.</summary>
    ConstructNotShip = 4,

    /// <summary>A ship cannot enter a construct frame in EA.</summary>
    ConstructIntoConstruct = 5,

    /// <summary>The world pose of the old or the new frame is unknown.</summary>
    PoseUnavailable = 6,

    /// <summary>The pose does not fit the float range of the new frame (<see cref="Horyzont.Core.Frames.FrameLimits"/>).</summary>
    OutOfRange = 7,

    /// <summary>The seat is not an occupied cell of the new construct frame, belongs to someone else, or is given for a non-player.</summary>
    InvalidSeat = 8,

    /// <summary>The reparent counter of the entity is exhausted.</summary>
    NotAllowed = 9,
}
