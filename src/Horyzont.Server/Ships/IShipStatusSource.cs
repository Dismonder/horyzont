using Horyzont.Core.Ids;
using Horyzont.Core.Ships;

namespace Horyzont.Server.Ships;

/// <summary>Flight status of ships for <see cref="ShipStatusSender"/> (K5-26); implemented by <see cref="ShipFlightSystem"/>.</summary>
public interface IShipStatusSource
{
    /// <summary>Gets the status of a ship.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="status">Receives the status.</param>
    /// <returns>False for a construct without a status.</returns>
    bool TryGetStatus(EntityId ship, out ShipFlightStatus status);
}
