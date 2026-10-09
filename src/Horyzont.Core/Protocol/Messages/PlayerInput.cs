namespace Horyzont.Core.Protocol.Messages;

/// <summary>Input of one client tick.</summary>
/// <param name="ClientTick">Client tick.</param>
/// <param name="MoveX">Strafe axis −1..1.</param>
/// <param name="MoveY">Forward axis −1..1.</param>
/// <param name="Yaw">
/// Body yaw in radians, in (-pi, pi]: the angle of the body's forward direction on the tangent plane at the feet, measured from
/// <c>SurfaceFrame.Reference</c> and right-handed about "up" (see <c>Horyzont.Core.Motion.SurfaceFrame</c>). The meaning is part of the
/// protocol: changing the convention requires a <see cref="ProtocolConstants.ProtocolVersion"/> bump.
/// </param>
/// <param name="Pitch">Camera pitch in radians.</param>
/// <param name="Buttons">Held buttons; only the bits of <see cref="ProtocolConstants.InputButtonsMask"/>.</param>
public readonly record struct PlayerInput(uint ClientTick, float MoveX, float MoveY, float Yaw, float Pitch, InputButtons Buttons);
