namespace Horyzont.Bots.Scripting;

/// <summary>What a bot wants in one physics tick: movement input and at most one tool action.</summary>
/// <param name="MoveX">Strafe axis −1..1.</param>
/// <param name="MoveY">Forward axis −1..1.</param>
/// <param name="Yaw">Body yaw in radians in the convention of <c>SurfaceFrame</c>.</param>
/// <param name="Pitch">Camera pitch in radians; negative looks down.</param>
/// <param name="Jump">Jump button.</param>
/// <param name="Sprint">Sprint button.</param>
/// <param name="Action">The tool action of this tick.</param>
public readonly record struct BotCommand(float MoveX, float MoveY, float Yaw, float Pitch, bool Jump, bool Sprint, BotAction Action)
{
    /// <summary>Gets a command that stands still and does nothing.</summary>
    public static BotCommand Idle(float yaw) => new(0f, 0f, yaw, 0f, false, false, BotAction.None);
}
