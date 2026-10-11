using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Motion;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Bots.Scripting;

/// <summary>
/// How a script command becomes player input, shared by the bot without an engine (<c>BotClient</c>) and the bot mode of the game
/// (<c>--bot</c>): the tool frame of an action and the sense of the way home. Both kinds of bots press the same buttons.
/// </summary>
public static class BotControls
{
    /// <summary>The tool frame a player would produce for an action; default (nothing pressed) for none and for the abuse actions.</summary>
    /// <param name="action">The action of the script.</param>
    /// <returns>The frame.</returns>
    public static ToolInputFrame FrameFor(BotAction action) => action switch
    {
        BotAction.Dig => new ToolInputFrame { SelectTool = ToolKind.TerrainManipulator, PrimaryPressed = true },
        BotAction.Fill => new ToolInputFrame { SelectTool = ToolKind.TerrainManipulator, SecondaryPressed = true },
        BotAction.PlaceBlock => new ToolInputFrame { SelectTool = ToolKind.Builder, SecondaryPressed = true },
        BotAction.RemoveBlock => new ToolInputFrame { SelectTool = ToolKind.Builder, PrimaryPressed = true },
        _ => default,
    };

    /// <summary>The direction and distance from the feet to home, in the tangent plane at the feet.</summary>
    /// <param name="feet">Feet in the planet frame.</param>
    /// <param name="home">Home in the planet frame.</param>
    /// <returns>The sense the script gets.</returns>
    public static BotSense SenseHome(Vector3 feet, Vector3 home)
    {
        var up = TerrainMath.UpAt(feet);
        var toHome = home - feet;
        var tangent = toHome - (up * Vector3.Dot(toHome, up));
        var reference = SurfaceFrame.Reference(up);
        var side = Vector3.Cross(up, reference);
        var yaw = MathF.Atan2(Vector3.Dot(tangent, side), Vector3.Dot(tangent, reference));
        return new BotSense(yaw, tangent.Length());
    }

    /// <summary>
    /// The mouse movement that turns a player from its current orientation to the yaw and pitch of a command (the bot mode of the
    /// game feeds it to the orientation tracker of the local player as one tick of mouse input).
    /// </summary>
    /// <param name="bodyRotation">Current body rotation.</param>
    /// <param name="up">Unit up at the feet.</param>
    /// <param name="pitch">Current pitch.</param>
    /// <param name="targetYaw">Yaw of the command (convention of <see cref="SurfaceFrame"/>).</param>
    /// <param name="targetPitch">Pitch of the command.</param>
    /// <returns>Yaw and pitch deltas in radians.</returns>
    public static (float Yaw, float Pitch) LookDelta(Quaternion bodyRotation, Vector3 up, float pitch, float targetYaw, float targetPitch) =>
        (Wrap(targetYaw - SurfaceFrame.YawOf(bodyRotation, up)), targetPitch - pitch);

    /// <summary>Wraps an angle into (-π, π].</summary>
    /// <param name="radians">Angle.</param>
    /// <returns>The wrapped angle.</returns>
    public static float Wrap(float radians)
    {
        var wrapped = MathF.IEEERemainder(radians, 2f * MathF.PI);
        return wrapped <= -MathF.PI ? wrapped + (2f * MathF.PI) : wrapped;
    }
}
