using System.Numerics;

using Horyzont.Core.Motion;

namespace Horyzont.Client.Movement;

/// <summary>State of the local body after one predicted tick, in the planet frame.</summary>
/// <param name="Feet">Feet position.</param>
/// <param name="Velocity">Velocity.</param>
/// <param name="BodyRotation">Body rotation.</param>
/// <param name="Pitch">Camera pitch in radians.</param>
/// <param name="Mode">Movement mode.</param>
/// <param name="OnFloor">True when the body stood on the floor (known to the client only; the server state does not carry it).</param>
public readonly record struct PredictedState(
    Vector3 Feet, Vector3 Velocity, Quaternion BodyRotation, float Pitch, MovementMode Mode, bool OnFloor);
