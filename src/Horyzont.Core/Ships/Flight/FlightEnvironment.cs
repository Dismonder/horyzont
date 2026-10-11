using Horyzont.Core.Frames;

namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Surroundings of a ship for one flight step (K5-24 decision 5), computed by the server flight and by the pilot prediction
/// (<see cref="FlightEnvironments"/>). Vectors are in the parent frame of the ship.
/// </summary>
public readonly record struct FlightEnvironment
{
    /// <summary>Gets the gravity acceleration, m/s².</summary>
    public Vector3D Gravity { get; init; }

    /// <summary>Gets the unit direction away from the centre of the nearest body.</summary>
    public Vector3D Up { get; init; }

    /// <summary>Gets the altitude above R plus the surface clearance of the nearest body (≥ 0), metres.</summary>
    public double Altitude { get; init; }

    /// <summary>Gets the radius R of the nearest body, metres.</summary>
    public double RadiusOfBody { get; init; }

    /// <summary>Gets the distance r from the centre of the nearest body, metres.</summary>
    public double DistanceFromBody { get; init; }

    /// <summary>Gets the atmosphere drag, 1/s (zero outside the atmosphere).</summary>
    public float DragPerSecond { get; init; }

    /// <summary>Gets the terrain gate.</summary>
    public ShipTerrainGate Gate { get; init; }

    /// <summary>Gets the launch block, the only launch block input of the model (K5 review N14).</summary>
    public ShipLaunchBlock LaunchBlock { get; init; }

    /// <summary>Gets a value indicating whether the parent frame is the system frame.</summary>
    public bool InSystemFrame { get; init; }

    /// <summary>Gets the distance from the system origin (system frame only), metres.</summary>
    public double SystemDistance { get; init; }

    /// <summary>
    /// Gets the cap of the speed across <see cref="Up"/>, m/s; infinite when none. The server sets it in the low band where the
    /// terrain collision is built only around the ship (K5-22 <c>TerrainSweep.MaxHorizontalSpeed</c>, README Y11).
    /// </summary>
    public double HorizontalSpeedCap { get; init; }
}
