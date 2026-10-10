using System.Numerics;

namespace Horyzont.Client.Camera;

/// <summary>Position and viewing direction of the camera.</summary>
/// <param name="Origin">Camera origin.</param>
/// <param name="Direction">Unit viewing direction.</param>
public readonly record struct CameraPose(Vector3 Origin, Vector3 Direction);
