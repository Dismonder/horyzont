using System.Numerics;

using Horyzont.Core.Motion;

namespace Horyzont.Core.Survival;

/// <summary>Detects the landing of a fall from two consecutive movement modes (K6-33, D18).</summary>
public static class FallImpact
{
    /// <summary>Detects a landing: the previous mode was <see cref="MovementMode.Falling"/> or <see cref="MovementMode.Jumping"/> and the mode is now walking or sprinting.</summary>
    /// <param name="previousMode">The mode of the previous tick.</param>
    /// <param name="previousVelocity">The velocity of the previous tick (after landing the engine zeroes the radial part).</param>
    /// <param name="up">The unit vector pointing up.</param>
    /// <param name="mode">The mode of this tick.</param>
    /// <param name="impactSpeed">The speed against the ground in m/s: the downward part of <paramref name="previousVelocity"/>, never negative.</param>
    /// <returns>True when the body landed.</returns>
    public static bool TryDetect(MovementMode previousMode, Vector3 previousVelocity, Vector3 up, MovementMode mode, out float impactSpeed) =>
        throw new NotImplementedException("K6-33 A6");
}
