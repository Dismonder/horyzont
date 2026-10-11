namespace Horyzont.Core.Ships.Flight;

/// <summary>What the terrain under a ship allows (K5-24 decisions 5 and 17, README Y11).</summary>
public enum ShipTerrainGate : byte
{
    /// <summary>The collision of the terrain under the ship is built.</summary>
    Ready = 0,

    /// <summary>The terrain is not loaded: the ship stays above 2 R.</summary>
    NotLoaded = 1,

    /// <summary>The terrain streams but the collision is not built: no descent under the pending altitude (hover).</summary>
    CollisionPending = 2,

    /// <summary>The terrain is unavailable (fault or timeout): no descent at any altitude.</summary>
    Unavailable = 3,
}
