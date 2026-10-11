namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Flight rules as data (K5-24 decision 3); not a format. Tuning changes no code (P5), but the flight model is shared with the
/// pilot prediction (K5-25), so the server and the client must use the same values.
/// </summary>
public sealed record FlightRules
{
    /// <summary>Gets the default rules.</summary>
    public static FlightRules Default { get; } = new();

    /// <summary>Gets the time constant τ of the speed limit v = clamp(h/τ, v_min, v_cap), seconds.</summary>
    public double TauSeconds { get; init; } = 8;

    /// <summary>Gets the lowest speed limit, m/s.</summary>
    public double MinSpeedLimit { get; init; } = 100;

    /// <summary>Gets the speed cap without the pulse drive, m/s.</summary>
    public double SpeedCap { get; init; } = 250;

    /// <summary>Gets the speed cap with the pulse drive engaged, m/s.</summary>
    public double PulseSpeedCap { get; init; } = 2000;

    /// <summary>Gets the time constant of the pulse drive holding the speed limit, seconds.</summary>
    public double PulseResponseSeconds { get; init; } = 1;

    /// <summary>Gets the fuel burnt per meganewton-second of thrust.</summary>
    public double FuelPerMegaNewtonSecond { get; init; } = 4;

    /// <summary>Gets the fuel burnt per second of the engaged pulse drive.</summary>
    public double PulseFuelPerSecond { get; init; } = 4;

    /// <summary>Gets the reserve as a fraction of the tank capacity: only landing is allowed in it.</summary>
    public double ReserveFraction { get; init; } = 0.1;

    /// <summary>Gets the speed under which a ship touching the ground with the gear down parks, m/s.</summary>
    public double ParkSpeed { get; init; } = 1;

    /// <summary>Gets the largest commanded turn rate, rad/s.</summary>
    public double MaxTurnRate { get; init; } = 1.5;

    /// <summary>Gets the gain of the assist damping the velocity of an axis without input, 1/s.</summary>
    public double AssistLinearGain { get; init; } = 2;

    /// <summary>Gets the gain of the assist damping the rotation of an axis without input, 1/s (reserved; the turn limit is torque based).</summary>
    public double AssistAngularGain { get; init; } = 4;

    /// <summary>Gets the atmosphere drag by body index (K5-21 <c>BodyPlacement.BodyIndex</c>), 1/s; missing index = 0.</summary>
    public IReadOnlyList<float> AtmosphereDragPerSecond { get; init; } = [0.05f, 0f, 0.05f, 0.06f];

    /// <summary>Gets the height of the highest relief of the generator over R; the altitude is measured above it, metres.</summary>
    public double SurfaceClearanceMeters { get; init; } = 300;

    /// <summary>Gets the seconds without a ready terrain after which the gate reports <c>Unavailable</c> (README Y11).</summary>
    public double ReadinessTimeoutSeconds { get; init; } = 20;

    /// <summary>Gets the floor of a ship whose terrain is not loaded, in body radii.</summary>
    public double NotLoadedFloorFactor { get; init; } = 2;

    /// <summary>Gets the altitude under which a pending collision stops the descent, metres.</summary>
    public double CollisionPendingAltitude { get; init; } = 400;

    /// <summary>Gets the distance from the system origin beyond which a ship cannot move outward, metres (inside the 500 km frame limit).</summary>
    public double SystemBoundaryMeters { get; init; } = 400_000;

    /// <summary>Gets the smallest cosine between a contact normal and the up direction that counts as ground for parking.</summary>
    public double GearContactMaxTilt { get; init; } = 0.7;

    /// <summary>Gets the friction factor of the tangential velocity after a contact.</summary>
    public double ContactFriction { get; init; } = 0.5;

    /// <summary>Speed limit at an altitude.</summary>
    /// <param name="altitude">Altitude above the clearance of the nearest body, metres.</param>
    /// <param name="pulse">True when the pulse drive is engaged.</param>
    /// <returns>The limit, m/s.</returns>
    public double SpeedLimit(double altitude, bool pulse) =>
        Math.Clamp(altitude / TauSeconds, MinSpeedLimit, Math.Max(MinSpeedLimit, pulse ? PulseSpeedCap : SpeedCap));

    /// <summary>Atmosphere drag of a body.</summary>
    /// <param name="bodyIndex">Body index; out of range = 0.</param>
    /// <returns>The drag, 1/s.</returns>
    public float DragOf(int bodyIndex) =>
        bodyIndex >= 0 && bodyIndex < AtmosphereDragPerSecond.Count ? AtmosphereDragPerSecond[bodyIndex] : 0f;
}
