using System.Buffers.Binary;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Core.Serialization;

/// <summary>Allocation-free canonical snapshot encoder into caller-owned storage.</summary>
public static class SnapshotWriter
{
    /// <summary>Writes exactly the snapshot wire format used by <see cref="MessageCodec"/>.</summary>
    /// <param name="destination">Storage, limited to the unreliable channel maximum.</param>
    /// <param name="snapshotId">Snapshot sequence.</param>
    /// <param name="tick">Server tick.</param>
    /// <param name="baselineSnapshotId">Acknowledged baseline or zero.</param>
    /// <param name="lastProcessedInputSeq">Last processed input.</param>
    /// <param name="frames">Unique frame table.</param>
    /// <param name="entities">Present entity fields.</param>
    /// <returns>Encoded byte count.</returns>
    public static int Write(Span<byte> destination, uint snapshotId, Tick tick, uint baselineSnapshotId,
        uint lastProcessedInputSeq, ReadOnlySpan<SnapshotFrame> frames, ReadOnlySpan<EntityState> entities)
    {
        if (frames.Length is < 1 or > 8 || entities.Length > 22)
        {
            throw new ArgumentException("Invalid snapshot counts.");
        }

        var writer = new Writer(destination[..Math.Min(destination.Length, 1150)]);
        writer.Byte((byte)MessageType.Snapshot);
        writer.Byte(1);
        writer.Var(snapshotId);
        writer.Var(tick.Value);
        writer.Var(baselineSnapshotId);
        writer.Var(lastProcessedInputSeq);
        writer.Var((ulong)frames.Length);
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            if (frame.Frame.Kind is < FrameKind.System or > FrameKind.Construct)
            {
                throw new ArgumentException("Invalid frame kind.");
            }

            for (var j = 0; j < i; j++)
            {
                if (frames[j].Frame == frame.Frame)
                {
                    throw new ArgumentException("Duplicate snapshot frame.");
                }
            }

            writer.Byte((byte)frame.Frame.Kind);
            writer.Var(frame.Frame.Id);
            writer.Var(frame.ParentSeq);
        }

        writer.Var((ulong)entities.Length);
        foreach (var state in entities)
        {
            if (state.FrameIndex >= frames.Length || ((byte)state.Fields & ~15) != 0)
            {
                throw new ArgumentException("Invalid entity fields or frame index.");
            }

            writer.Var(state.Entity.Value);
            writer.Byte((byte)state.Fields);
            writer.Byte(state.FrameIndex);
            writer.Var(state.ParentSeq);
            if ((state.Fields & EntityStateFields.Position) != 0)
            {
                writer.Single(state.Position.X);
                writer.Single(state.Position.Y);
                writer.Single(state.Position.Z);
            }

            if ((state.Fields & EntityStateFields.Rotation) != 0)
            {
                writer.UInt32(Quantization.PackRotation(state.Rotation));
            }

            if ((state.Fields & EntityStateFields.Velocity) != 0)
            {
                writer.Half(state.Velocity.X);
                writer.Half(state.Velocity.Y);
                writer.Half(state.Velocity.Z);
            }

            if ((state.Fields & EntityStateFields.MotionFlags) != 0)
            {
                writer.Byte(state.MotionFlags);
            }
        }

        return writer.Length;
    }

    private ref struct Writer
    {
        private readonly Span<byte> _buffer;

        public Writer(Span<byte> buffer) => _buffer = buffer;

        public int Length { get; private set; }

        private Span<byte> Take(int count)
        {
            if (count > _buffer.Length - Length)
            {
                throw new ArgumentException("Snapshot does not fit the destination or channel limit.");
            }

            var slice = _buffer.Slice(Length, count);
            Length += count;
            return slice;
        }

        public void Byte(byte value) => Take(1)[0] = value;

        public void Var(ulong value)
        {
            do
            {
                var part = (byte)(value & 127);
                value >>= 7;
                Byte(value == 0 ? part : (byte)(part | 128));
            }
            while (value != 0);
        }

        public void UInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), value);

        public void Single(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("Non-finite position.");
            }

            BinaryPrimitives.WriteSingleLittleEndian(Take(4), value);
        }

        public void Half(float value)
        {
            if (!float.IsFinite(value) || Math.Abs(value) > 65504f)
            {
                throw new ArgumentException("Velocity exceeds finite half range.");
            }

            BinaryPrimitives.WriteHalfLittleEndian(Take(2), (Half)value);
        }
    }
}
