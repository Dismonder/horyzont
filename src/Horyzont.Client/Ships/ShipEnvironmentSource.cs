using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Ships;

/// <summary>Environment of a ship motion on the client (planet or system; K5-25 <c>ClientFlightEnvironment</c>).</summary>
/// <param name="motion">The motion.</param>
/// <param name="stats">Ship stats (centre of mass).</param>
/// <returns>The environment without the server-only inputs.</returns>
public delegate FlightEnvironment ShipEnvironmentSource(in ShipMotion motion, ShipStats stats);
