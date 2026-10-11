using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Ships;

/// <summary>Flight status of a ship for the HUD (K5-24 decision 12); K5-26 sends it in <c>ShipStatus</c> (31).</summary>
/// <param name="Fuel">Fuel units.</param>
/// <param name="FuelCapacity">Tank capacity.</param>
/// <param name="SpeedLimit">Current speed limit, m/s.</param>
/// <param name="Flags">Flight flags.</param>
/// <param name="LaunchBlock">Why the ship may not take off.</param>
public readonly record struct ShipFlightStatus(float Fuel, float FuelCapacity, float SpeedLimit, ShipFlightFlags Flags, ShipLaunchBlock LaunchBlock);
