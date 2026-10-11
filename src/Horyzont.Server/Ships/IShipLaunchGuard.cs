using Horyzont.Core.Ids;
using Horyzont.Core.Ships;

namespace Horyzont.Server.Ships;

/// <summary>Decides at a launch attempt of a parked ship whether persons in its hull block it (K5-25 decision 9).</summary>
public interface IShipLaunchGuard
{
    /// <summary>Evaluates a launch attempt; may seat persons in the hull.</summary>
    /// <param name="ship">The parked ship.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns><see cref="ShipLaunchBlock.None"/> or <see cref="ShipLaunchBlock.PersonInHull"/>.</returns>
    ShipLaunchBlock Evaluate(EntityId ship, Tick tick);
}
