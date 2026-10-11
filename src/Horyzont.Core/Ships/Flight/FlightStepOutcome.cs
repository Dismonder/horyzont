namespace Horyzont.Core.Ships.Flight;

/// <summary>Result of one <see cref="ShipFlightModel.Step"/>; the proposed pose is in the motion itself.</summary>
/// <param name="SpeedLimit">The speed limit of the step, m/s.</param>
/// <param name="ThrustNewtons">Sum of the absolute thrust used on the three control axes, N.</param>
/// <param name="FuelUsed">Fuel burnt in the step.</param>
public readonly record struct FlightStepOutcome(double SpeedLimit, double ThrustNewtons, double FuelUsed);
