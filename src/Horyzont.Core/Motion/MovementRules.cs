namespace Horyzont.Core.Motion;

/// <summary>
/// Constants of character movement. Movement is outside the journal (no <c>Apply</c> or <c>Replay</c> reads it), so the values are
/// plain <c>init</c> properties; client and server use the same ones because the build hash in the handshake forces the same build.
/// </summary>
public sealed record MovementRules
{
    /// <summary>Walking speed in m/s.</summary>
    public float WalkSpeed { get; init; } = 4.5f;

    /// <summary>Sprinting speed in m/s.</summary>
    public float SprintSpeed { get; init; } = 7.5f;

    /// <summary>Upward speed given by a jump in m/s (about 1.27 m at 9.81 m/s²).</summary>
    public float JumpSpeed { get; init; } = 5.0f;

    /// <summary>Horizontal acceleration towards the wanted velocity on the ground in m/s².</summary>
    public float GroundAcceleration { get; init; } = 40f;

    /// <summary>Horizontal deceleration on the ground when no direction is held in m/s².</summary>
    public float GroundDeceleration { get; init; } = 50f;

    /// <summary>Horizontal acceleration in the air in m/s².</summary>
    public float AirAcceleration { get; init; } = 8f;

    /// <summary>Largest downward speed in m/s.</summary>
    public float MaxFallSpeed { get; init; } = 60f;

    /// <summary>Steepest slope that counts as floor, in degrees (used by the engine body).</summary>
    public float FloorMaxAngleDegrees { get; init; } = 46f;

    /// <summary>Distance below the feet within which the body sticks to the floor, in metres.</summary>
    public float FloorSnapLength { get; init; } = 0.3f;

    /// <summary>Largest camera pitch in radians (89 degrees).</summary>
    public float MaxPitch { get; init; } = 1.5533f;

    /// <summary>Developer flight speed in m/s.</summary>
    public float DevFlySpeed { get; init; } = 20f;

    /// <summary>Developer flight speed with sprint in m/s.</summary>
    public float DevFlySprintSpeed { get; init; } = 150f;

    /// <summary>Developer flight stays within this multiple of the planet radius from the centre (float precision of the terrain).</summary>
    public float MaxDevFlightRadiusFactor { get; init; } = 2.25f;

    /// <summary>Ticks without input after which the server steps with a neutral input (100 ms at 60 Hz).</summary>
    public int MaxStarvedTicks { get; init; } = 6;

    /// <summary>Ticks to wait for terrain collision under a frozen body before it is released (30 s at 60 Hz).</summary>
    public int GroundWaitDeadlineTicks { get; init; } = 1800;

    /// <summary>A body this far below the core boundary is moved back above the surface, in metres.</summary>
    public float CoreRescueMarginMeters { get; init; } = 2f;
}
