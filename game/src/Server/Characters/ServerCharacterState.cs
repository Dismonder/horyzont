using Horyzont.Core.Ids;
using Horyzont.Core.Motion;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Server.Characters;

/// <summary>The state of one server body at the end of the latest tick.</summary>
/// <param name="Planet">The planet.</param>
/// <param name="Frame">The frame of the player in the simulation.</param>
/// <param name="ParentSeq">The reparent counter of the simulation.</param>
/// <param name="Feet">Feet position in the planet frame.</param>
/// <param name="BodyRotation">Normalized body rotation.</param>
/// <param name="Pitch">Camera pitch of the last input.</param>
/// <param name="Velocity">Velocity in the planet frame.</param>
/// <param name="Mode">Movement mode.</param>
/// <param name="LastProcessedInputSeq">Number of the last input the body consumed.</param>
public readonly record struct ServerCharacterState(
    PlanetId Planet, FrameRef Frame, uint ParentSeq, NumVector3 Feet, NumQuaternion BodyRotation, float Pitch, NumVector3 Velocity,
    MovementMode Mode, uint LastProcessedInputSeq);
