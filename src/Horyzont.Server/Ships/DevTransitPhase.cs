namespace Horyzont.Server.Ships;

/// <summary>Phase of the <see cref="DevTransitPilot"/>.</summary>
public enum DevTransitPhase : byte
{
    /// <summary>Upright lift off.</summary>
    Departure = 0,

    /// <summary>Turn the nose to the target while climbing.</summary>
    Turn = 4,

    /// <summary>Fly to the target (pulse drive or full forward thrust).</summary>
    Cruise = 1,

    /// <summary>Upright descent with a braking profile over the measured ground.</summary>
    Landing = 2,

    /// <summary>Parked on the target.</summary>
    Arrived = 3,
}
