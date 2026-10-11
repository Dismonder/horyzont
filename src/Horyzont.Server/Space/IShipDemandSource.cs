using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Space;

/// <summary>
/// Decides whether a ship-mode construct in a planet frame demands the terrain of that planet (K5-22 decision 2b, README Y11).
/// K5-22 ships <see cref="MovingShipDemand"/> (the ship moved more than 0.5 m in the last 30 ticks); K5-24 replaces it with
/// <c>UnparkedShipDemand</c> (<c>Mode == Ship</c> and not <c>Parked</c>). Flight never takes terrain leases. Main thread; allocation-free.
/// </summary>
public interface IShipDemandSource
{
    /// <summary>Gets a value indicating whether the ship demands the terrain of its parent planet now.</summary>
    /// <param name="ship">A construct in <see cref="ConstructMode.Ship"/> whose parent is a planet frame.</param>
    /// <returns>True when the planet must stay (or become) active for this ship.</returns>
    bool Demands(Construct ship);
}
