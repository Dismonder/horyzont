using System.Numerics;

using Horyzont.Core.Motion;

namespace Horyzont.Client.Camera;

/// <summary>
/// Continuous orientation of the local body and its pitch. The client never reconstructs the view from the <c>Yaw</c> angle (which
/// jumps near the poles of the reference frame); it keeps a rotation, carries it along the surface by parallel transport every tick,
/// turns it with the mouse, and derives the <c>Yaw</c> of each input from it.
/// </summary>
public sealed class OrientationTracker
{
    private readonly float _maxPitch;

    /// <summary>Initializes a new instance of the <see cref="OrientationTracker"/> class.</summary>
    /// <param name="rules">Movement rules (pitch limit).</param>
    public OrientationTracker(MovementRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _maxPitch = rules.MaxPitch;
        BodyRotation = Quaternion.Identity;
    }

    /// <summary>Gets the body rotation, normalized.</summary>
    public Quaternion BodyRotation { get; private set; }

    /// <summary>Gets the pitch in radians, within the movement rules limit.</summary>
    public float Pitch { get; private set; }

    /// <summary>Sets the orientation after a spawn, a teleport or a correction.</summary>
    /// <param name="bodyRotation">New body rotation.</param>
    /// <param name="pitch">New pitch.</param>
    public void Reset(Quaternion bodyRotation, float pitch)
    {
        BodyRotation = Quaternion.Normalize(bodyRotation);
        Pitch = Math.Clamp(pitch, -_maxPitch, _maxPitch);
    }

    /// <summary>Carries the orientation to the new up and applies the mouse movement of the tick.</summary>
    /// <param name="newUp">Unit up at the predicted feet position.</param>
    /// <param name="mouseYawDelta">Turn about up in radians; positive turns to the left (right-handed about up, like <c>Yaw</c>).</param>
    /// <param name="mousePitchDelta">Change of pitch in radians; positive looks up.</param>
    public void Advance(Vector3 newUp, float mouseYawDelta, float mousePitchDelta)
    {
        var carried = SurfaceFrame.TransportToUp(BodyRotation, newUp);
        if (mouseYawDelta != 0f)
        {
            carried = Quaternion.Normalize(Quaternion.Concatenate(carried, Quaternion.CreateFromAxisAngle(newUp, mouseYawDelta)));
        }

        BodyRotation = carried;
        Pitch = Math.Clamp(Pitch + mousePitchDelta, -_maxPitch, _maxPitch);
    }

    /// <summary>The <c>Yaw</c> to send for the current orientation, relative to the up the server will use.</summary>
    /// <param name="up">Unit up at the predicted feet position before the step.</param>
    /// <returns>The yaw in (-pi, pi].</returns>
    public float YawFor(Vector3 up) => SurfaceFrame.YawOf(BodyRotation, up);
}
