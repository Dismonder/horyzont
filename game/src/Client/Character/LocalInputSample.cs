using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Game.Client.Character;

/// <summary>Everything the local player asked for since the previous tick.</summary>
/// <param name="MoveX">Strafe, -1 (left) to 1 (right).</param>
/// <param name="MoveY">Walk, -1 (back) to 1 (forward).</param>
/// <param name="Buttons">Held buttons (jump, sprint, crouch); the dev-fly bit is added by the player.</param>
/// <param name="MouseYaw">Turn since the last tick in radians, positive to the left.</param>
/// <param name="MousePitch">Pitch change since the last tick in radians, positive looks up.</param>
/// <param name="WheelSteps">Mouse wheel notches since the last tick (positive = away from the user).</param>
public readonly record struct LocalInputSample(float MoveX, float MoveY, InputButtons Buttons, float MouseYaw, float MousePitch, int WheelSteps = 0)
{
    /// <summary>Gets the sample of a player who does nothing.</summary>
    public static LocalInputSample None => default;
}
