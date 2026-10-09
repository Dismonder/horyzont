namespace Horyzont.Client.Movement;

/// <summary>Result of validating the own-character part of a snapshot.</summary>
public enum OwnStateVerdict : byte
{
    /// <summary>The state is valid and newer than the previous one.</summary>
    Accepted = 0,

    /// <summary>The snapshot has no state of the own entity.</summary>
    MissingOwnEntity = 1,

    /// <summary>The state is a delta or lacks position, rotation, velocity or motion flags.</summary>
    MissingFields = 2,

    /// <summary>The frame index points outside the snapshot frames.</summary>
    FrameIndexOutOfRange = 3,

    /// <summary>The frame is not a planet announced in the server hello.</summary>
    UnknownPlanet = 4,

    /// <summary>A position or velocity component is NaN or infinite.</summary>
    NonFinite = 5,

    /// <summary>The rotation is not a unit quaternion within the tolerance.</summary>
    BadRotation = 6,

    /// <summary>The snapshot id or the tick is not newer than the last accepted snapshot.</summary>
    Stale = 7,

    /// <summary>The server claims to have processed an input the client never sent.</summary>
    AckBeyondSent = 8,

    /// <summary>The acknowledged input number is lower than in the previous accepted snapshot.</summary>
    AckRegressed = 9,
}
