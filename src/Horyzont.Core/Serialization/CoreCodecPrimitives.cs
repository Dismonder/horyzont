using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Serialization;

/// <summary>Internal shared primitive encoders and decoders for Core types.</summary>
internal static class CoreCodecPrimitives
{
    /// <summary>True for every defined <see cref="RejectReason"/>: 0-21 (Planet) and the Space reasons 30, 31, 37, 39.</summary>
    public static bool IsKnownRejectReason(byte value) =>
        value <= (byte)RejectReason.ServerReadOnly
        || value is (byte)RejectReason.NoCockpit or (byte)RejectReason.ShipAnchored or (byte)RejectReason.NoEngine or (byte)RejectReason.ShipNotParked
        || value is (>= (byte)RejectReason.SeatOccupied and <= (byte)RejectReason.NotPilot) or (byte)RejectReason.ConstructNotEmpty;

    public static void WriteFrameRef(ByteWriter w, FrameRef f)
    {
        if (f.Kind < FrameKind.System || f.Kind > FrameKind.Construct)
        {
            throw new ArgumentException($"Invalid FrameKind {(byte)f.Kind}.", nameof(f));
        }

        w.WriteByte((byte)f.Kind);
        w.WriteVarUInt(f.Id);
    }

    public static FrameRef ReadFrameRef(ref ByteReader r)
    {
        byte kindByte = r.ReadByte();
        if (kindByte < (byte)FrameKind.System || kindByte > (byte)FrameKind.Construct)
        {
            throw new ProtocolViolationException($"Invalid FrameKind {kindByte}.");
        }

        ulong id = r.ReadVarUInt();
        return new FrameRef((FrameKind)kindByte, id);
    }

    public static void WritePlatformIdentity(ByteWriter w, PlatformIdentity identity)
    {
        if (identity.Platform < IdentityPlatform.Dev || identity.Platform > IdentityPlatform.Steam)
        {
            throw new ArgumentException($"Invalid IdentityPlatform {(byte)identity.Platform}.", nameof(identity));
        }

        w.WriteByte((byte)identity.Platform);
        w.WriteUInt64(identity.Value);
    }

    public static PlatformIdentity ReadPlatformIdentity(ref ByteReader r)
    {
        byte platformByte = r.ReadByte();
        if (platformByte < (byte)IdentityPlatform.Dev || platformByte > (byte)IdentityPlatform.Steam)
        {
            throw new ProtocolViolationException($"Invalid IdentityPlatform {platformByte}.");
        }

        ulong val = r.ReadUInt64();
        return new PlatformIdentity((IdentityPlatform)platformByte, val);
    }

    public static void WriteCellPos(ByteWriter w, CellPos cell)
    {
        w.WriteVarInt(cell.X);
        w.WriteVarInt(cell.Y);
        w.WriteVarInt(cell.Z);
    }

    public static CellPos ReadCellPos(ref ByteReader r)
    {
        int x = r.ReadVarInt32();
        int y = r.ReadVarInt32();
        int z = r.ReadVarInt32();
        return new CellPos(x, y, z);
    }

    public static void WriteOptionalCellPos(ByteWriter w, CellPos? cell)
    {
        if (cell.HasValue)
        {
            w.WriteBool(true);
            WriteCellPos(w, cell.Value);
        }
        else
        {
            w.WriteBool(false);
        }
    }

    public static CellPos? ReadOptionalCellPos(ref ByteReader r)
    {
        bool hasValue = r.ReadBool();
        return hasValue ? ReadCellPos(ref r) : null;
    }

    public static void WriteChunkPos(ByteWriter w, ChunkPos chunk)
    {
        w.WriteVarInt(chunk.X);
        w.WriteVarInt(chunk.Y);
        w.WriteVarInt(chunk.Z);
    }

    public static ChunkPos ReadChunkPos(ref ByteReader r)
    {
        int x = r.ReadVarInt32();
        int y = r.ReadVarInt32();
        int z = r.ReadVarInt32();
        return new ChunkPos(x, y, z);
    }

    public static void WriteBlockState(ByteWriter w, BlockState block)
    {
        if (!block.IsWellFormed)
        {
            throw new ArgumentException("Block state is not well-formed.", nameof(block));
        }

        w.WriteUInt32(block.Packed);
    }

    public static BlockState ReadBlockState(ref ByteReader r)
    {
        uint packed = r.ReadUInt32();
        var block = new BlockState(packed);
        if (!block.IsWellFormed)
        {
            throw new ProtocolViolationException("Malformed block state.");
        }

        return block;
    }
}
