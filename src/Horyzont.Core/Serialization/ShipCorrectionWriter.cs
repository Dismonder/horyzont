using System.Buffers.Binary;
using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Serialization;

/// <summary>
/// Allocation-free encoder of a <see cref="ShipCorrection"/> frame (K5-25 decision 15): the same bytes as
/// <see cref="MessageCodec"/>, written straight into the frame buffer of the replication round.
/// </summary>
public static class ShipCorrectionWriter
{
    /// <summary>Largest frame: type, version, a 10-byte ship id and the 71 fixed bytes.</summary>
    public const int MaxBytes = 2 + 10 + 71;

    /// <summary>Writes one frame.</summary>
    /// <param name="destination">At least <see cref="MaxBytes"/> bytes.</param>
    /// <param name="ship">The ship.</param>
    /// <param name="lastProcessedSeq">Input slot of the step.</param>
    /// <param name="parentSeq">Reparent counter of the ship.</param>
    /// <param name="position">Position in the parent frame.</param>
    /// <param name="rotation">Rotation in the parent frame (unit).</param>
    /// <param name="linearVelocity">Linear velocity in the parent frame.</param>
    /// <param name="angularVelocity">Angular velocity in construct axes.</param>
    /// <param name="fuel">Fuel, not negative.</param>
    /// <param name="flags">Flight flags.</param>
    /// <param name="gate">Terrain gate.</param>
    /// <param name="launchBlock">Launch block.</param>
    /// <param name="horizontalSpeedCap">Positive cap or +infinity.</param>
    /// <returns>The frame length.</returns>
    public static int Write(
        Span<byte> destination, EntityId ship, uint lastProcessedSeq, uint parentSeq, Vector3 position, Quaternion rotation,
        Vector3 linearVelocity, Vector3 angularVelocity, float fuel, ShipFlightFlags flags, ShipTerrainGate gate, ShipLaunchBlock launchBlock,
        float horizontalSpeedCap)
    {
        if (!(fuel >= 0f) || !float.IsFinite(fuel) || !(horizontalSpeedCap > 0f) || gate > ShipTerrainGate.Unavailable
            || launchBlock > ShipLaunchBlock.PersonInHull || !IsFinite(position) || !IsFinite(linearVelocity) || !IsFinite(angularVelocity)
            || !float.IsFinite(rotation.Length()) || MathF.Abs(rotation.Length() - 1f) > 1e-3f)
        {
            throw new ArgumentException("Invalid ship correction values.");
        }

        var i = 0;
        destination[i++] = (byte)MessageType.ShipCorrection;
        destination[i++] = 1;
        var id = ship.Value;
        while (id >= 0x80)
        {
            destination[i++] = (byte)(id | 0x80);
            id >>= 7;
        }

        destination[i++] = (byte)id;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[i..], lastProcessedSeq);
        i += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[i..], parentSeq);
        i += 4;
        i = Vector(destination, i, position);
        i = Single(destination, i, rotation.X);
        i = Single(destination, i, rotation.Y);
        i = Single(destination, i, rotation.Z);
        i = Single(destination, i, rotation.W);
        i = Vector(destination, i, linearVelocity);
        i = Vector(destination, i, angularVelocity);
        i = Single(destination, i, fuel);
        destination[i++] = (byte)flags;
        destination[i++] = (byte)gate;
        destination[i++] = (byte)launchBlock;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[i..], BitConverter.SingleToUInt32Bits(horizontalSpeedCap));
        return i + 4;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static int Vector(Span<byte> d, int i, Vector3 v) => Single(d, Single(d, Single(d, i, v.X), v.Y), v.Z);

    private static int Single(Span<byte> d, int i, float v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(d[i..], v);
        return i + 4;
    }
}
