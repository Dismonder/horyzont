namespace Horyzont.Core.Intents;

/// <summary>
/// Why an intent was rejected. Ranges: 0–20 K1, 21–29 K2–K4, 30–39 Space, 40–59 Survival, 60–69 Combat, 70–79 EA content.
/// </summary>
public enum RejectReason : byte
{
    /// <summary>Accepted.</summary>
    None = 0,

    /// <summary>The player does not exist.</summary>
    UnknownPlayer = 1,

    /// <summary>The player is offline.</summary>
    PlayerOffline = 2,

    /// <summary>The rate limit of the intent class is exhausted.</summary>
    RateLimited = 3,

    /// <summary>Non-finite numbers, non-normalized directions or values out of range.</summary>
    InvalidParameters = 4,

    /// <summary>The ray does not start near the player's eye.</summary>
    RayOriginMismatch = 5,

    /// <summary>The ray hits nothing within reach.</summary>
    NoHit = 6,

    /// <summary>The target is beyond reach.</summary>
    OutOfReach = 7,

    /// <summary>The terrain of the area is not loaded on the server.</summary>
    AreaNotLoaded = 8,

    /// <summary>The edit would reach below the planet core limit.</summary>
    BelowCore = 9,

    /// <summary>The edit is above the atmosphere.</summary>
    OutsideAtmosphere = 10,

    /// <summary>The planet does not exist.</summary>
    UnknownPlanet = 11,

    /// <summary>The construct does not exist.</summary>
    UnknownConstruct = 12,

    /// <summary>The cell is outside −2048..2047.</summary>
    CellOutOfBounds = 13,

    /// <summary>The cell is occupied.</summary>
    CellOccupied = 14,

    /// <summary>The cell is empty.</summary>
    CellEmpty = 15,

    /// <summary>The block type does not exist.</summary>
    UnknownBlockType = 16,

    /// <summary>The material does not exist.</summary>
    UnknownMaterial = 17,

    /// <summary>The change would intersect a player (or remove a seat or a frame a player uses).</summary>
    IntersectsPlayer = 18,

    /// <summary>Something blocks the line from the eye to the target.</summary>
    NoLineOfSight = 19,

    /// <summary>The target is not on the player's planet.</summary>
    WrongPlanet = 20,

    /// <summary>The server is in safe mode (a write error or an unreplayable journal tail) and changes nothing; K2-10.</summary>
    ServerReadOnly = 21,

    /// <summary>The construct has no cockpit (or the change would remove the last cockpit of a ship); K5-23.</summary>
    NoCockpit = 30,

    /// <summary>A block of the construct is embedded in terrain; K5-23.</summary>
    ShipAnchored = 31,

    /// <summary>The construct has no engine (or the change would remove the last engine of a ship); K5-23.</summary>
    NoEngine = 37,
}
