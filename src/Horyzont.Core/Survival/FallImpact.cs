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
    /// <returns>True when the body landed; false, with an impact speed of 0, when <paramref name="previousVelocity"/> or <paramref name="up"/> is not finite.</returns>
    public static bool TryDetect(MovementMode previousMode, Vector3 previousVelocity, Vector3 up, MovementMode mode, out float impactSpeed)
    {
        impactSpeed = 0f;
        if (previousMode is not (MovementMode.Falling or MovementMode.Jumping) || mode is not (MovementMode.Walking or MovementMode.Sprinting))
        {
            return false;
        }

        if (!float.IsFinite(previousVelocity.X) || !float.IsFinite(previousVelocity.Y) || !float.IsFinite(previousVelocity.Z)
            || !float.IsFinite(up.X) || !float.IsFinite(up.Y) || !float.IsFinite(up.Z))
        {
            return false;
        }

        impactSpeed = MathF.Max(0f, -Vector3.Dot(previousVelocity, up));
        return true;
    }
}
