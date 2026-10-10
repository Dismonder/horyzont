using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Tools;

/// <summary>
/// Chooses the block orientation (one of the 24 of <see cref="BlockOrientation"/>): quarter turns about the up axis of the block and a
/// change of the up face over the six faces; every one of the 24 orientations is reachable.
/// </summary>
public static class OrientationPicker
{
    /// <summary>Turns the block about its up axis.</summary>
    /// <param name="orientation">Current orientation.</param>
    /// <param name="quarterTurns">Signed quarter turns.</param>
    /// <returns>The new orientation.</returns>
    public static byte Yaw(byte orientation, int quarterTurns) => BlockOrientation.WithYaw(orientation, quarterTurns);

    /// <summary>Moves the up face over the six faces; the yaw index stays.</summary>
    /// <param name="orientation">Current orientation.</param>
    /// <param name="steps">Signed number of faces.</param>
    /// <returns>The new orientation.</returns>
    public static byte Up(byte orientation, int steps)
    {
        var slot = (((BlockOrientation.UpSlot(orientation) + steps) % 6) + 6) % 6;
        return BlockOrientation.WithUp(orientation, slot);
    }
}
