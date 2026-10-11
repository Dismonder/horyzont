namespace Horyzont.Core.Ships.Flight;

/// <summary>Pilot buttons as states, not edges (K5-24 decision 1, P9): robust to lost and repeated inputs.</summary>
[Flags]
public enum ShipButtons : byte
{
    /// <summary>No button.</summary>
    None = 0,

    /// <summary>The flight assist is on.</summary>
    Assist = 1,

    /// <summary>The pulse drive is engaged.</summary>
    PulseDrive = 2,

    /// <summary>The landing gear is down.</summary>
    LandingGear = 4,
}
