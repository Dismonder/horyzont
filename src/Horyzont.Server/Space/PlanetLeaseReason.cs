namespace Horyzont.Server.Space;

/// <summary>Why a planet terrain is held active by a lease (K5-22 decision 4; written to the log).</summary>
public enum PlanetLeaseReason : byte
{
    /// <summary>The journal replay edits the planet.</summary>
    Replay = 0,

    /// <summary>A system settles something on the planet before players arrive.</summary>
    Settle = 1,

    /// <summary>A developer command (<c>place</c>, dev tools).</summary>
    Dev = 2,

    /// <summary>Another system, for example the local client of a host.</summary>
    External = 3,
}
