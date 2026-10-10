using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Ships;

/// <summary>Thrust in newtons along the six construct axes: the force pushes the ship toward the named face.</summary>
/// <param name="PosX">Thrust toward +X.</param>
/// <param name="NegX">Thrust toward −X.</param>
/// <param name="PosY">Thrust toward +Y.</param>
/// <param name="NegY">Thrust toward −Y.</param>
/// <param name="PosZ">Thrust toward +Z.</param>
/// <param name="NegZ">Thrust toward −Z.</param>
public readonly record struct ShipThrust(float PosX, float NegX, float PosY, float NegY, float PosZ, float NegZ)
{
    /// <summary>Thrust toward a face.</summary>
    /// <param name="face">The face.</param>
    public float this[Face face] => face switch
    {
        Face.PosX => PosX,
        Face.NegX => NegX,
        Face.PosY => PosY,
        Face.NegY => NegY,
        Face.PosZ => PosZ,
        Face.NegZ => NegZ,
        _ => throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown face."),
    };
}
