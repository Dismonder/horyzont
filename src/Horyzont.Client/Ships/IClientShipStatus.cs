using Horyzont.Core.Ids;
using Horyzont.Core.Ships;

namespace Horyzont.Client.Ships;

/// <summary>
/// Flight status of ships on the client (K5-24 decision 20): fuel, limit, flags and the launch block. K5-26 implements it from
/// <c>ShipStatus</c> (31); until then <see cref="UnknownShipStatus"/> answers nothing and the HUD shows dashes.
/// </summary>
public interface IClientShipStatus
{
    /// <summary>Gets the latest status of a ship.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="status">Receives the status.</param>
    /// <returns>False when the client knows no status.</returns>
    bool TryGet(EntityId ship, out ShipFlightStatus status);
}

/// <summary>No status known (before K5-26).</summary>
public sealed class UnknownShipStatus : IClientShipStatus
{
    /// <summary>Gets the shared instance.</summary>
    public static UnknownShipStatus Instance { get; } = new();

    /// <inheritdoc />
    public bool TryGet(EntityId ship, out ShipFlightStatus status)
    {
        status = default;
        return false;
    }
}
