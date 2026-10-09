using System.Numerics;

namespace Horyzont.Client.Movement;

/// <summary>
/// When the predicted body has diverged from the server and must be moved back. K2-11 only snaps and replays; smoothing of small
/// errors and tuning for latency belong to K3-14, which changes these values and adds its own policy.
/// </summary>
public sealed record CorrectionPolicy
{
    /// <summary>Gets the largest position error that is still in sync, in metres.</summary>
    public float SnapDistance { get; init; } = 0.02f;

    /// <summary>Gets the largest velocity error that is still in sync, in m/s.</summary>
    public float VelocityTolerance { get; init; } = 0.1f;

    /// <summary>Gets the number of unacknowledged inputs at which prediction pauses (2 s at 60 Hz).</summary>
    public int MaxUnacknowledged { get; init; } = 120;

    /// <summary>Tells whether the server state differs from the predicted one by more than the tolerances.</summary>
    /// <param name="predicted">State the client predicted for an input.</param>
    /// <param name="server">State the server reports after the same input.</param>
    /// <returns>True when the client must correct.</returns>
    public bool NeedsCorrection(in PredictedState predicted, in PredictedState server) =>
        PositionError(predicted, server) > SnapDistance
        || VelocityError(predicted, server) > VelocityTolerance
        || predicted.Mode != server.Mode;

    /// <summary>Distance between the feet of two states in metres.</summary>
    /// <param name="a">First state.</param>
    /// <param name="b">Second state.</param>
    /// <returns>The distance.</returns>
    public static float PositionError(in PredictedState a, in PredictedState b) => Vector3.Distance(a.Feet, b.Feet);

    /// <summary>Difference of the velocities of two states in m/s.</summary>
    /// <param name="a">First state.</param>
    /// <param name="b">Second state.</param>
    /// <returns>The difference.</returns>
    public static float VelocityError(in PredictedState a, in PredictedState b) => Vector3.Distance(a.Velocity, b.Velocity);
}
