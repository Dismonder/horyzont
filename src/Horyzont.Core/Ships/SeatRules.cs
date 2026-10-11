namespace Horyzont.Core.Ships;

/// <summary>Seat, ejection, launch guard and pilot input rules (K5-25); part of <see cref="Simulation.GameRules"/>.</summary>
public sealed record SeatRules
{
    /// <summary>Default rules.</summary>
    public static SeatRules Default { get; } = new();

    /// <summary>Largest distance from the eye to the centre of a seat cell to sit in it or free it, metres (P1).</summary>
    public float ReachMeters { get; init; } = 3f;

    /// <summary>Height of the feet of a seated player above the bottom face of the seat cell, metres.</summary>
    public float FeetLift { get; init; } = 0.05f;

    /// <summary>Eye height of a seated player above the feet, metres.</summary>
    public float SeatedEyeHeight { get; init; } = 1.2f;

    /// <summary>Largest ship speed for an ejection, m/s.</summary>
    public float EjectMaxSpeed { get; init; } = 30f;

    /// <summary>Largest ship height above the terrain for an ejection, metres.</summary>
    public float EjectMaxHeight { get; init; } = 200f;

    /// <summary>Margin added to the hull boxes when looking for persons in the hull at a launch attempt, metres.</summary>
    public float HullMarginMeters { get; init; } = 0.5f;

    /// <summary>The launch guard evaluates a ship at most once per this many ticks.</summary>
    public int LaunchRetryTicks { get; init; } = 30;

    /// <summary>Seat persons in the hull on free passenger or navigator seats at a launch attempt (P2).</summary>
    public bool AutoSeatOnLaunch { get; init; } = true;

    /// <summary>Most steps of a sight ray through the seat cell of a seated actor; a walk that runs out has no line of sight.</summary>
    public int SightSteps { get; init; } = 64;

    /// <summary>Input slots the server keeps between the newest received pilot input and the consumed one (R1).</summary>
    public int InputDelayTicks { get; init; } = 2;

    /// <summary>Missing pilot inputs replaced by the last control before the axes go neutral.</summary>
    public int HoldLastInputTicks { get; init; } = 15;
}
