namespace Horyzont.Core.Ships;

/// <summary>
/// Why a parked ship may not take off (K5-24 decision 12); the only launch block input of the flight model. Value 2
/// (<c>PersonInHull</c>) belongs to K5-25.
/// </summary>
public enum ShipLaunchBlock : byte
{
    /// <summary>Nothing blocks the launch.</summary>
    None = 0,

    /// <summary>The fuel is in the reserve: only landing is allowed.</summary>
    FuelReserve = 1,
}
