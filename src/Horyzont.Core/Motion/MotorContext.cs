using System.Numerics;

using Horyzont.Core.World;

namespace Horyzont.Core.Motion;

/// <summary>Body state before a motor step. In the planet frame.</summary>
/// <param name="Feet">Feet position.</param>
/// <param name="Velocity">Velocity the body actually has (after collision response).</param>
/// <param name="OnFloor">True when the body stands on the floor.</param>
/// <param name="PreviousMode">Mode of the previous step.</param>
/// <param name="Planet">Planet the body is on.</param>
/// <param name="DevFlightAllowed">True when developer flight may be used (otherwise the button is ignored).</param>
public readonly record struct MotorContext(
    Vector3 Feet, Vector3 Velocity, bool OnFloor, MovementMode PreviousMode, PlanetParams Planet, bool DevFlightAllowed);
