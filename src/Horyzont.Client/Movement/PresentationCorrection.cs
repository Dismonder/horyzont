using System.Numerics;

using Horyzont.Core.Motion;

namespace Horyzont.Client.Movement;

/// <summary>Visual correction independent of the immediately reconciled physics state.</summary>
public sealed class PresentationCorrection
{
    private const float Duration = 100f;
    private Vector3 _startPosition;
    private Quaternion _startRotation = Quaternion.Identity;
    private float _elapsed;

    /// <summary>Gets the planet-local translation applied only to presentation.</summary>
    public Vector3 PositionOffset { get; private set; }

    /// <summary>Gets the rotation multiplied on the left of the current body rotation.</summary>
    public Quaternion RotationOffset { get; private set; } = Quaternion.Identity;

    /// <summary>Gets whether the latest reconciliation required an immediate visual snap.</summary>
    public bool Snapped { get; private set; }

    /// <summary>Preserves the visible pose across a complete physics replay, or snaps at discontinuities.</summary>
    /// <param name="before">Current physics state before replay.</param>
    /// <param name="after">Current physics state after all replay steps.</param>
    /// <param name="discontinuity">Missing history, teleport, initialization or parent change.</param>
    public void Reconcile(in PredictedState before, in PredictedState after, bool discontinuity)
    {
        var distance = Vector3.Distance(before.Feet, after.Feet);
        var dot = MathF.Abs(Quaternion.Dot(before.BodyRotation, after.BodyRotation));
        if (discontinuity || before.Mode == MovementMode.Frozen || after.Mode == MovementMode.Frozen
            || distance >= 1f || dot <= MathF.Cos(MathF.PI / 8f))
        {
            Reset();
            Snapped = true;
            return;
        }

        _startPosition = before.Feet + PositionOffset - after.Feet;
        _startRotation = Quaternion.Normalize(RotationOffset * before.BodyRotation * Quaternion.Inverse(after.BodyRotation));
        PositionOffset = _startPosition;
        RotationOffset = _startRotation;
        _elapsed = 0;
        Snapped = false;
    }

    /// <summary>Linearly removes the current correction over 100 ms without allocating.</summary>
    /// <param name="deltaSeconds">Elapsed presentation time, finite and nonnegative.</param>
    public void Advance(float deltaSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);
        if (!float.IsFinite(deltaSeconds)) { throw new ArgumentOutOfRangeException(nameof(deltaSeconds)); }
        _elapsed = MathF.Min(Duration, _elapsed + deltaSeconds * 1000f);
        var fraction = _elapsed / Duration;
        PositionOffset = _startPosition * (1f - fraction);
        RotationOffset = Quaternion.Normalize(Quaternion.Slerp(_startRotation, Quaternion.Identity, fraction));
    }

    /// <summary>Forgets all presentation offsets.</summary>
    public void Reset()
    {
        _startPosition = PositionOffset = Vector3.Zero;
        _startRotation = RotationOffset = Quaternion.Identity;
        _elapsed = Duration;
        Snapped = false;
    }
}
