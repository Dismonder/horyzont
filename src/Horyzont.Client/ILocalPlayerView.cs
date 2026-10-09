using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Client;

/// <summary>State of the local player at the end of the latest client physics tick (not render-interpolated), planet frame.</summary>
public interface ILocalPlayerView
{
    /// <summary>Gets a value indicating whether the player is spawned, not frozen and not disconnected.</summary>
    bool IsActive { get; }

    /// <summary>Gets the planet whose frame the values below are in.</summary>
    PlanetId Planet { get; }

    /// <summary>Gets the predicted feet position.</summary>
    Vector3 PredictedFeet { get; }

    /// <summary>Gets the body rotation, normalized.</summary>
    Quaternion BodyRotation { get; }

    /// <summary>Gets the camera pitch in radians.</summary>
    float Pitch { get; }

    /// <summary>Gets the eye position: feet + UpAt(feet) * EyeHeight, exactly the server formula.</summary>
    Vector3 Eye { get; }

    /// <summary>
    /// Gets the camera origin: the eye in first person, behind the shoulder after the spring arm in third person, the developer
    /// camera in <see cref="CameraMode.DevFree"/>.
    /// </summary>
    Vector3 CameraOrigin { get; }

    /// <summary>Gets the unit, finite camera direction.</summary>
    Vector3 CameraDirection { get; }

    /// <summary>Gets the camera mode.</summary>
    CameraMode CameraMode { get; }
}
