namespace Horyzont.Core.Ships.Flight;

/// <summary>Flags of the flight state (K5-24 decision 4). The server flight sets the terrain flags; the model only reads them.</summary>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name fixed by the K5-24 plan; a [Flags] enum of state bits.")]
public enum ShipFlightFlags : byte
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The flight assist is on.</summary>
    Assist = 1,

    /// <summary>The pulse drive is engaged.</summary>
    PulseEngaged = 2,

    /// <summary>The landing gear is down.</summary>
    GearDown = 4,

    /// <summary>The ship is parked.</summary>
    Parked = 8,

    /// <summary>The fuel is in the reserve.</summary>
    Reserve = 16,

    /// <summary>The autopilot lands the ship (no pilot).</summary>
    Autoland = 32,

    /// <summary>The terrain gate limits the descent (<c>NotLoaded</c> or <c>CollisionPending</c>).</summary>
    TerrainHold = 64,

    /// <summary>The terrain is unavailable: no descent at any altitude (decision 17).</summary>
    TerrainUnavailable = 128,
}
