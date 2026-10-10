using System.Numerics;

namespace Horyzont.Core.Motion;

/// <summary>Result of a motor step.</summary>
/// <param name="DesiredVelocity">Velocity the caller applies (MoveAndSlide or an ideal integrator), planet frame.</param>
/// <param name="Up">Surface up at the feet before the step.</param>
/// <param name="BodyRotation">Body rotation, normalized: <see cref="SurfaceFrame.FromYaw"/> of the input yaw.</param>
/// <param name="Mode">Movement mode after the step.</param>
public readonly record struct MotorOutput(Vector3 DesiredVelocity, Vector3 Up, Quaternion BodyRotation, MovementMode Mode);
