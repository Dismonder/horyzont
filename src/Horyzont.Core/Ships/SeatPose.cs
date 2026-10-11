using System.Numerics;

using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Ships;

/// <summary>
/// Pose of a player seated in a seat block, in the frame of the construct (K5-25 decision 2). The resolver, the launch guard and
/// the client camera use the same code.
/// </summary>
public static class SeatPose
{
    /// <summary>Feet: the centre of the bottom face of the cell in block axes, lifted by <see cref="SeatRules.FeetLift"/> along the block up.</summary>
    /// <param name="cell">Seat cell.</param>
    /// <param name="orientation">Block orientation.</param>
    /// <param name="rules">Seat rules.</param>
    /// <returns>Feet in construct space.</returns>
    public static Vector3 Feet(CellPos cell, byte orientation, SeatRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var up = UpOf(orientation);
        return new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) - (up * 0.5f) + (up * rules.FeetLift);
    }

    /// <summary>Body rotation of the seated player: the block rotation (the player faces the block forward).</summary>
    /// <param name="orientation">Block orientation.</param>
    /// <returns>Rotation in construct space.</returns>
    public static Quaternion Rotation(byte orientation) => BlockOrientation.ToQuaternion(orientation);

    /// <summary>Eye of the seated player: feet plus <see cref="SeatRules.SeatedEyeHeight"/> along the block up.</summary>
    /// <param name="cell">Seat cell.</param>
    /// <param name="orientation">Block orientation.</param>
    /// <param name="rules">Seat rules.</param>
    /// <returns>Eye in construct space.</returns>
    public static Vector3 Eye(CellPos cell, byte orientation, SeatRules rules) =>
        Feet(cell, orientation, rules) + (UpOf(orientation) * rules.SeatedEyeHeight);

    /// <summary>Unit up of a block orientation in construct space.</summary>
    /// <param name="orientation">Block orientation.</param>
    /// <returns>The axis.</returns>
    public static Vector3 UpOf(byte orientation) => Normal(BlockOrientation.Up(orientation));

    /// <summary>Unit forward of a block orientation in construct space.</summary>
    /// <param name="orientation">Block orientation.</param>
    /// <returns>The axis.</returns>
    public static Vector3 ForwardOf(byte orientation) => Normal(BlockOrientation.Forward(orientation));

    internal static Vector3 Normal(Face face) => face switch
    {
        Face.PosX => Vector3.UnitX,
        Face.NegX => -Vector3.UnitX,
        Face.PosY => Vector3.UnitY,
        Face.NegY => -Vector3.UnitY,
        Face.PosZ => Vector3.UnitZ,
        _ => -Vector3.UnitZ,
    };
}
