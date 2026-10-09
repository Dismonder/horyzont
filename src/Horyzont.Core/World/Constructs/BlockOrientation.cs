using System.Numerics;

namespace Horyzont.Core.World.Constructs;

/// <summary>
/// The 24 orientations of a block. FORMAT: the index is stored in <see cref="BlockState"/> (journal, checkpoints,
/// protocol), so the table never changes. Index = UpSlot * 4 + Yaw. Identity (0) = Up +Y, Forward −Z.
/// Right = Forward × Up. Yaw k = Forward of yaw 0 turned k quarter turns counter-clockwise about Up (right-hand rule).
/// </summary>
public static class BlockOrientation
{
    /// <summary>Number of orientations; equals <see cref="BlockState.OrientationCount"/>.</summary>
    public const int Count = 24;

    /// <summary>The identity orientation: Up +Y, Forward −Z.</summary>
    public const byte Identity = 0;

    private const byte PosX = (byte)Face.PosX;
    private const byte NegX = (byte)Face.NegX;
    private const byte PosY = (byte)Face.PosY;
    private const byte NegY = (byte)Face.NegY;
    private const byte PosZ = (byte)Face.PosZ;
    private const byte NegZ = (byte)Face.NegZ;

    // FORMAT: (Up, Forward) of every orientation index. A literal, never computed; changing it changes the meaning of stored blocks.
    private static ReadOnlySpan<byte> UpForward =>
    [
        PosY, NegZ, PosY, NegX, PosY, PosZ, PosY, PosX,
        NegY, PosZ, NegY, NegX, NegY, NegZ, NegY, PosX,
        PosX, NegZ, PosX, PosY, PosX, PosZ, PosX, NegY,
        NegX, NegZ, NegX, NegY, NegX, PosZ, NegX, PosY,
        PosZ, PosY, PosZ, NegX, PosZ, NegY, PosZ, PosX,
        NegZ, NegY, NegZ, NegX, NegZ, PosY, NegZ, PosX,
    ];

    // Face of each up slot: 0 PosY, 1 NegY, 2 PosX, 3 NegX, 4 PosZ, 5 NegZ.
    private static ReadOnlySpan<byte> SlotFaces => [PosY, NegY, PosX, NegX, PosZ, NegZ];

    /// <summary>True when the value is below <see cref="Count"/>.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <returns>Whether it is a valid orientation.</returns>
    public static bool IsValid(byte orientation) => orientation < Count;

    /// <summary>Up slot 0-5: 0 +Y, 1 −Y, 2 +X, 3 −X, 4 +Z, 5 −Z.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <returns>The slot.</returns>
    public static int UpSlot(byte orientation)
    {
        Check(orientation);
        return orientation >> 2;
    }

    /// <summary>Yaw index 0-3.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <returns>The yaw.</returns>
    public static int Yaw(byte orientation)
    {
        Check(orientation);
        return orientation & 3;
    }

    /// <summary>Construct face an up slot stands for.</summary>
    /// <param name="slot">Slot 0-5.</param>
    /// <returns>The face.</returns>
    public static Face UpOfSlot(int slot)
    {
        if ((uint)slot >= 6)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Up slot must be 0-5.");
        }

        return (Face)SlotFaces[slot];
    }

    /// <summary>Image of block-local +Y in construct space.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <returns>The face.</returns>
    public static Face Up(byte orientation)
    {
        Check(orientation);
        return (Face)UpForward[orientation * 2];
    }

    /// <summary>Image of block-local −Z in construct space.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <returns>The face.</returns>
    public static Face Forward(byte orientation)
    {
        Check(orientation);
        return (Face)UpForward[(orientation * 2) + 1];
    }

    /// <summary>Image of block-local +X in construct space.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <returns>The face.</returns>
    public static Face Right(byte orientation)
    {
        Check(orientation);
        return CrossFaces(Forward(orientation), Up(orientation));
    }

    /// <summary>Finds the orientation with the given Up and Forward.</summary>
    /// <param name="up">Image of block-local +Y.</param>
    /// <param name="forward">Image of block-local −Z.</param>
    /// <param name="orientation">The orientation when the faces are perpendicular.</param>
    /// <returns>False when the faces lie on the same axis.</returns>
    public static bool TryFromUpForward(Face up, Face forward, out byte orientation)
    {
        orientation = 0;
        if (up > Face.NegZ || forward > Face.NegZ || ((byte)up >> 1) == ((byte)forward >> 1))
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (UpForward[i * 2] == (byte)up && UpForward[(i * 2) + 1] == (byte)forward)
            {
                orientation = (byte)i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the orientation with the given Up and Forward.</summary>
    /// <param name="up">Image of block-local +Y.</param>
    /// <param name="forward">Image of block-local −Z.</param>
    /// <returns>The orientation.</returns>
    /// <exception cref="ArgumentException">The faces are not perpendicular.</exception>
    public static byte FromUpForward(Face up, Face forward) =>
        TryFromUpForward(up, forward, out var orientation)
            ? orientation
            : throw new ArgumentException($"Up {up} and forward {forward} are not perpendicular.");

    /// <summary>Turns the block about its Up; Up stays.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <param name="quarterTurns">Quarter turns, any integer (modulo 4).</param>
    /// <returns>The turned orientation.</returns>
    public static byte WithYaw(byte orientation, int quarterTurns)
    {
        Check(orientation);
        return (byte)((orientation & ~3) | ((orientation + (quarterTurns & 3)) & 3));
    }

    /// <summary>Changes the up slot and keeps the yaw index.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <param name="upSlot">New slot 0-5.</param>
    /// <returns>The changed orientation.</returns>
    public static byte WithUp(byte orientation, int upSlot)
    {
        Check(orientation);
        if ((uint)upSlot >= 6)
        {
            throw new ArgumentOutOfRangeException(nameof(upSlot), upSlot, "Up slot must be 0-5.");
        }

        return (byte)((upSlot << 2) | (orientation & 3));
    }

    /// <summary>Block-local face to construct face.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <param name="blockFace">Face in block space.</param>
    /// <returns>The face in construct space.</returns>
    public static Face ToConstruct(byte orientation, Face blockFace)
    {
        Check(orientation);
        return blockFace switch
        {
            Face.PosX => Right(orientation),
            Face.NegX => Opposite(Right(orientation)),
            Face.PosY => Up(orientation),
            Face.NegY => Opposite(Up(orientation)),
            Face.PosZ => Opposite(Forward(orientation)),
            Face.NegZ => Forward(orientation),
            _ => throw new ArgumentOutOfRangeException(nameof(blockFace), blockFace, "Unknown face."),
        };
    }

    /// <summary>Construct face to block-local face; inverse of <see cref="ToConstruct"/>.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <param name="constructFace">Face in construct space.</param>
    /// <returns>The face in block space.</returns>
    public static Face ToBlock(byte orientation, Face constructFace)
    {
        Check(orientation);
        if (constructFace > Face.NegZ)
        {
            throw new ArgumentOutOfRangeException(nameof(constructFace), constructFace, "Unknown face.");
        }

        for (var i = 0; i < 6; i++)
        {
            if (ToConstruct(orientation, (Face)i) == constructFace)
            {
                return (Face)i;
            }
        }

        throw new InvalidOperationException("Orientation table is not a bijection.");
    }

    /// <summary>Rotation from block space to construct space; exact for the 24 rotations.</summary>
    /// <param name="orientation">Orientation index.</param>
    /// <returns>The rotation.</returns>
    public static Quaternion ToQuaternion(byte orientation)
    {
        Check(orientation);
        var x = Normal(Right(orientation));
        var y = Normal(Up(orientation));
        var z = -Normal(Forward(orientation));

        // System.Numerics uses row vectors: the rows of the matrix are the images of the block axes.
        var matrix = new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
    }

    private static void Check(byte orientation)
    {
        if (orientation >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Orientation must be below 24.");
        }
    }

    private static Face Opposite(Face face) => (Face)((byte)face ^ 1);

    private static Vector3 Normal(Face face) => face switch
    {
        Face.PosX => Vector3.UnitX,
        Face.NegX => -Vector3.UnitX,
        Face.PosY => Vector3.UnitY,
        Face.NegY => -Vector3.UnitY,
        Face.PosZ => Vector3.UnitZ,
        _ => -Vector3.UnitZ,
    };

    private static Face FromNormal(Vector3 normal)
    {
        if (normal.X > 0.5f)
        {
            return Face.PosX;
        }

        if (normal.X < -0.5f)
        {
            return Face.NegX;
        }

        if (normal.Y > 0.5f)
        {
            return Face.PosY;
        }

        if (normal.Y < -0.5f)
        {
            return Face.NegY;
        }

        return normal.Z > 0.5f ? Face.PosZ : Face.NegZ;
    }

    private static Face CrossFaces(Face a, Face b) => FromNormal(Vector3.Cross(Normal(a), Normal(b)));
}
