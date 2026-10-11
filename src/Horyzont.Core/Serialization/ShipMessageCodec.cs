using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Core.Serialization;

/// <summary>Payloads of <see cref="ShipInputBatch"/> (30) and <see cref="ShipCorrection"/> (34), K5-25 decisions 11 and 14.</summary>
internal static class ShipMessageCodec
{
    private const int InputBytes = 11;
    private const byte KnownButtons = (byte)(ShipButtons.Assist | ShipButtons.PulseDrive | ShipButtons.LandingGear);

    internal static void WriteInputBatch(ByteWriter w, ShipInputBatch batch)
    {
        if (batch.Inputs.Count < 1 || batch.Inputs.Count > ProtocolConstants.MaxInputsPerBatch)
        {
            throw new ArgumentException($"ShipInputBatch inputs count {batch.Inputs.Count} must be 1..{ProtocolConstants.MaxInputsPerBatch}.", nameof(batch));
        }

        w.WriteUInt32(batch.FirstSeq);
        w.WriteByte((byte)batch.Inputs.Count);
        for (var i = 0; i < batch.Inputs.Count; i++)
        {
            var input = batch.Inputs[i];
            var c = input.Control;
            if (c.ThrustX == sbyte.MinValue || c.ThrustY == sbyte.MinValue || c.ThrustZ == sbyte.MinValue
                || c.Pitch == sbyte.MinValue || c.Yaw == sbyte.MinValue || c.Roll == sbyte.MinValue)
            {
                throw new ArgumentException("Ship control axis -128 is not permitted.", nameof(batch));
            }

            if (((byte)c.Buttons & ~KnownButtons) != 0)
            {
                throw new ArgumentException($"Unknown ship buttons {(byte)c.Buttons}.", nameof(batch));
            }

            w.WriteUInt32(input.ClientTick);
            w.WriteSByte(c.ThrustX);
            w.WriteSByte(c.ThrustY);
            w.WriteSByte(c.ThrustZ);
            w.WriteSByte(c.Pitch);
            w.WriteSByte(c.Yaw);
            w.WriteSByte(c.Roll);
            w.WriteByte((byte)c.Buttons);
        }
    }

    internal static ShipInputBatch ReadInputBatch(ref ByteReader r)
    {
        var firstSeq = r.ReadUInt32();
        var count = r.ReadByte();
        if (count < 1 || count > ProtocolConstants.MaxInputsPerBatch)
        {
            throw new ProtocolViolationException($"ShipInputBatch input count {count} must be 1..{ProtocolConstants.MaxInputsPerBatch}.");
        }

        if (r.Remaining < count * InputBytes)
        {
            throw new ProtocolViolationException("Not enough data for declared ship inputs.");
        }

        var inputs = new ShipInput[count];
        for (var i = 0; i < count; i++)
        {
            var tick = r.ReadUInt32();
            var tx = r.ReadSByte();
            var ty = r.ReadSByte();
            var tz = r.ReadSByte();
            var pitch = r.ReadSByte();
            var yaw = r.ReadSByte();
            var roll = r.ReadSByte();
            var buttons = r.ReadByte();
            if ((buttons & ~KnownButtons) != 0)
            {
                throw new ProtocolViolationException($"Unknown ship buttons {buttons}.");
            }

            inputs[i] = new ShipInput(tick, new ShipControl(tx, ty, tz, pitch, yaw, roll, (ShipButtons)buttons));
        }

        return new ShipInputBatch(firstSeq, inputs);
    }

    internal static void WriteCorrection(ByteWriter w, ShipCorrection c)
    {
        if (!(c.Fuel >= 0f) || !float.IsFinite(c.Fuel))
        {
            throw new ArgumentException($"Fuel {c.Fuel} must be finite and not negative.", nameof(c));
        }

        if (!IsCap(c.HorizontalSpeedCap))
        {
            throw new ArgumentException($"Horizontal speed cap {c.HorizontalSpeedCap} must be positive or +infinity.", nameof(c));
        }

        if (c.Gate > ShipTerrainGate.Unavailable || c.LaunchBlock > ShipLaunchBlock.PersonInHull)
        {
            throw new ArgumentException("Unknown gate or launch block.", nameof(c));
        }

        w.WriteVarUInt(c.Ship.Value);
        w.WriteUInt32(c.LastProcessedSeq);
        w.WriteUInt32(c.ParentSeq);
        w.WriteVector3(c.Position);
        w.WriteQuaternion(c.Rotation);
        w.WriteVector3(c.LinearVelocity);
        w.WriteVector3(c.AngularVelocity);
        w.WriteSingle(c.Fuel);
        w.WriteByte((byte)c.Flags);
        w.WriteByte((byte)c.Gate);
        w.WriteByte((byte)c.LaunchBlock);
        w.WriteUInt32(BitConverter.SingleToUInt32Bits(c.HorizontalSpeedCap));
    }

    internal static ShipCorrection ReadCorrection(ref ByteReader r)
    {
        var ship = new EntityId(r.ReadVarUInt());
        var seq = r.ReadUInt32();
        var parentSeq = r.ReadUInt32();
        var position = r.ReadVector3();
        var rotation = r.ReadQuaternion();
        var linear = r.ReadVector3();
        var angular = r.ReadVector3();
        var fuel = r.ReadSingle();
        if (fuel < 0f)
        {
            throw new ProtocolViolationException($"Negative fuel {fuel}.");
        }

        var flags = (ShipFlightFlags)r.ReadByte();
        var gate = r.ReadByte();
        if (gate > (byte)ShipTerrainGate.Unavailable)
        {
            throw new ProtocolViolationException($"Unknown terrain gate {gate}.");
        }

        var block = r.ReadByte();
        if (block > (byte)ShipLaunchBlock.PersonInHull)
        {
            throw new ProtocolViolationException($"Unknown launch block {block}.");
        }

        var cap = BitConverter.UInt32BitsToSingle(r.ReadUInt32());
        if (!IsCap(cap))
        {
            throw new ProtocolViolationException($"Horizontal speed cap {cap} must be positive or +infinity.");
        }

        return new ShipCorrection(ship, seq, parentSeq, position, rotation, linear, angular, fuel, flags, (ShipTerrainGate)gate, (ShipLaunchBlock)block, cap);
    }

    private static bool IsCap(float cap) => cap > 0f && !float.IsNaN(cap);
}
