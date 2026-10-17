using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Serialization;

/// <summary>Internal shared primitive encoders and decoders for Core types.</summary>
internal static class CoreCodecPrimitives
{
    /// <summary>True for every defined <see cref="RejectReason"/>: the Planet reasons 0-21, the Space reasons 30-39 and the survival reasons 40-43.</summary>
    public static bool IsKnownRejectReason(byte value) => Enum.IsDefined((RejectReason)value);

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

    public static void WriteSlotRef(ByteWriter w, SlotRef slot)
    {
        w.WriteVarUInt(slot.Container.Value);
        w.WriteVarUInt(slot.Slot);
    }

    public static SlotRef ReadSlotRef(ref ByteReader r)
    {
        var container = new ContainerId(r.ReadVarUInt());
        ushort slot = r.ReadVarUInt16();
        return new SlotRef(container, slot);
    }

    public static byte ReadEnumByte(ref ByteReader r, byte first, byte last, string what)
    {
        byte value = r.ReadByte();
        if (value < first || value > last)
        {
            throw new ProtocolViolationException($"Unknown {what} {value}.");
        }

        return value;
    }

    public static void WriteContainerHost(ByteWriter w, ContainerHost host)
    {
        if (!Enum.IsDefined(host.Kind))
        {
            throw new ArgumentException($"Invalid ContainerHostKind {(byte)host.Kind}.", nameof(host));
        }

        w.WriteByte((byte)host.Kind);
        w.WriteVarUInt(host.Id);
        switch (host.Kind)
        {
            case ContainerHostKind.ConstructBlock:
                WriteCellPos(w, host.Cell);
                break;
            case ContainerHostKind.Poi:
                w.WriteVarUInt(host.Site);
                w.WriteVarUInt(host.Number);
                break;
        }
    }

    public static ContainerHost ReadContainerHost(ref ByteReader r)
    {
        var kind = (ContainerHostKind)ReadEnumByte(ref r, (byte)ContainerHostKind.Player, (byte)ContainerHostKind.Poi, "container host kind");
        ulong id = r.ReadVarUInt();
        return kind switch
        {
            ContainerHostKind.ConstructBlock => new ContainerHost(kind, id, ReadCellPos(ref r), 0, 0),
            ContainerHostKind.Poi => new ContainerHost(kind, id, default, r.ReadVarUInt(), r.ReadVarUInt()),
            _ => new ContainerHost(kind, id, default, 0, 0),
        };
    }

    /// <summary>A non-empty stack: varuint type (1..4096), varuint count (at least 1), instance byte 0.</summary>
    public static void WriteItemStack(ByteWriter w, ItemStack stack)
    {
        if (stack.IsEmpty || stack.Type.Value > ProtocolConstants.MaxItemTypes || stack.Count == 0)
        {
            throw new ArgumentException($"Invalid item stack {stack.Type.Value} x {stack.Count}.", nameof(stack));
        }

        if (stack.Instance is not null)
        {
            throw new ArgumentException("Item instance data is not supported in format 1.", nameof(stack));
        }

        w.WriteVarUInt(stack.Type.Value);
        w.WriteVarUInt(stack.Count);
        w.WriteByte(0);
    }

    public static ItemStack ReadItemStack(ref ByteReader r)
    {
        ushort type = r.ReadVarUInt16();
        if (type == 0 || type > ProtocolConstants.MaxItemTypes)
        {
            throw new ProtocolViolationException($"Item type {type} must be in 1..{ProtocolConstants.MaxItemTypes}.");
        }

        ushort count = r.ReadVarUInt16();
        if (count == 0)
        {
            throw new ProtocolViolationException("Item stack count 0.");
        }

        byte instance = r.ReadByte();
        if (instance != 0)
        {
            throw new ProtocolViolationException($"Item instance byte {instance} is not supported in format 1.");
        }

        return new ItemStack(new ItemTypeId(type), count);
    }
}
