using Horyzont.Core.Ids;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Server.Ships;

/// <summary>
/// Pilot input of ships (K5-24 decision 10). K5-25 implements it from <c>ShipInputBatch</c>; without control the flight autolands
/// (spec 02:202). Main thread; allocation-free.
/// </summary>
public interface IShipControlSource
{
    /// <summary>Gets the control of a ship for a tick.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="tick">The server tick.</param>
    /// <param name="control">Receives the control.</param>
    /// <returns>False when nobody controls the ship (the flight autolands).</returns>
    bool TryGetControl(EntityId ship, Tick tick, out ShipControl control);
}

/// <summary>No pilot anywhere: every ship autolands.</summary>
public sealed class NoPilotControlSource : IShipControlSource
{
    /// <summary>Gets the shared instance.</summary>
    public static NoPilotControlSource Instance { get; } = new();

    /// <inheritdoc />
    public bool TryGetControl(EntityId ship, Tick tick, out ShipControl control)
    {
        control = default;
        return false;
    }
}
