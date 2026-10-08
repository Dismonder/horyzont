namespace Horyzont.Core.Protocol.Messages;

/// <summary>Input of one client tick.</summary>
/// <param name="ClientTick">Client tick.</param>
/// <param name="MoveX">Strafe axis −1..1.</param>
/// <param name="MoveY">Forward axis −1..1.</param>
/// <param name="Yaw">Body yaw in radians.</param>
/// <param name="Pitch">Camera pitch in radians.</param>
/// <param name="Buttons">Held buttons.</param>
public readonly record struct PlayerInput(uint ClientTick, float MoveX, float MoveY, float Yaw, float Pitch, InputButtons Buttons);
