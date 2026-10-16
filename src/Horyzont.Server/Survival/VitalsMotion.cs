using System.Numerics;

using Horyzont.Core.Motion;

namespace Horyzont.Server.Survival;

/// <summary>
/// What the vitals service needs to know about the body of a player in one tick (K6-33 D13): the movement mode, the velocity in the frame of the planet (for the
/// speed of a landing), the feet and whether the body is attached to something (a seat), which exempts it from falls and from the cost of sprinting.
/// </summary>
/// <param name="Mode">The movement mode of this tick.</param>
/// <param name="Velocity">The velocity of this tick, in the frame the body is in.</param>
/// <param name="Feet">The feet, in the frame the body is in (the up direction of the fall comes from here).</param>
/// <param name="Attached">Whether the body is attached (seated or otherwise carried).</param>
public readonly record struct VitalsMotion(MovementMode Mode, Vector3 Velocity, Vector3 Feet, bool Attached);
