using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Serialization;

/// <summary>Internal shared primitive encoders and decoders for Core types.</summary>
internal static class CoreCodecPrimitives
{
    /// <summary>True for every defined <see cref="RejectReason"/>: the Planet reasons 0-21, the Space reasons 30-39 and the survival reasons 40-43 and 54, 57.</summary>
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

    /// <summary>
    /// The three food slots (K6-33): for each slot a varuint type (0 for an empty slot, else 1..4096) and, for a type other than 0 only, a varuint tick of
    /// eating. Shared by operation 41, blob 22 and message 48.
    /// </summary>
    public static void WriteFoodSlots(ByteWriter w, FoodSlots slots)
    {
        for (var i = 0; i < FoodSlots.Count; i++)
        {
            var slot = slots[i];
            if (slot.Type.Value > ProtocolConstants.MaxItemTypes)
            {
                throw new ArgumentException($"A food type is 1 to {ProtocolConstants.MaxItemTypes}, not {slot.Type.Value}.", nameof(slots));
            }

            if (slot.IsEmpty && slot.Eaten.Value != 0)
            {
                throw new ArgumentException("An empty food slot has no tick.", nameof(slots));
            }

            w.WriteVarUInt(slot.Type.Value);
            if (!slot.IsEmpty)
            {
                w.WriteVarUInt(slot.Eaten.Value);
            }
        }
    }

    public static FoodSlots ReadFoodSlots(ref ByteReader r)
    {
        Span<FoodSlot> read = stackalloc FoodSlot[FoodSlots.Count];
        for (var i = 0; i < read.Length; i++)
        {
            ushort type = r.ReadVarUInt16();
            if (type > ProtocolConstants.MaxItemTypes)
            {
                throw new ProtocolViolationException($"A food type is 1 to {ProtocolConstants.MaxItemTypes}, not {type}.");
            }

            read[i] = type == 0 ? default : new FoodSlot(new ItemTypeId(type), new Tick(r.ReadVarUInt()));
        }

        return new FoodSlots(read[0], read[1], read[2]);
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

    /// <summary>A block state: a kind byte (0 = no state, only when <paramref name="allowNone"/>) and the fields of the kind (K6-30).</summary>
    public static void WriteBlockEntityState(ByteWriter w, BlockEntityState? state, bool allowNone)
    {
        switch (state)
        {
            case null when allowNone:
                w.WriteByte(0);
                break;
            case ChestBlockState chest:
                w.WriteByte((byte)BlockEntityKind.Chest);
                w.WriteVarUInt(chest.Container.Value);
                break;
            case BedBlockState bed:
                w.WriteByte((byte)BlockEntityKind.Bed);
                w.WriteVarUInt(bed.Owner.Value);
                break;
            case StationBlockState station:
                ArgumentNullException.ThrowIfNull(station.Recipe);
                if (station.Started is not null && station.Recipe.Length == 0)
                {
                    throw new ArgumentException("A started station needs a recipe.", nameof(state));
                }

                w.WriteByte((byte)BlockEntityKind.Station);
                w.WriteVarUInt(station.Input.Value);
                w.WriteVarUInt(station.Output.Value);
                w.WriteString(station.Recipe, ProtocolConstants.MaxRecipeKeyBytes);
                if (station.Started is { } started)
                {
                    w.WriteByte(1);
                    w.WriteVarUInt(started.Value);
                }
                else
                {
                    w.WriteByte(0);
                }

                break;
            default:
                throw new ArgumentException($"A block state of format 1 is a chest, a bed or a station, not {state?.GetType().Name ?? "null"}.", nameof(state));
        }
    }

    public static BlockEntityState? ReadBlockEntityState(ref ByteReader r, bool allowNone)
    {
        byte kind = r.ReadByte();
        switch (kind)
        {
            case 0 when allowNone:
                return null;
            case (byte)BlockEntityKind.Chest:
                return new ChestBlockState(new ContainerId(r.ReadVarUInt()));
            case (byte)BlockEntityKind.Bed:
                return new BedBlockState(new PlayerId(r.ReadVarUInt()));
            case (byte)BlockEntityKind.Station:
                var input = new ContainerId(r.ReadVarUInt());
                var output = new ContainerId(r.ReadVarUInt());
                var recipe = r.ReadString(ProtocolConstants.MaxRecipeKeyBytes);
                byte flags = r.ReadByte();
                if (flags > 1)
                {
                    throw new ProtocolViolationException($"Unknown station flags {flags}.");
                }

                Tick? started = flags == 1 ? new Tick(r.ReadVarUInt()) : null;
                if (started is not null && recipe.Length == 0)
                {
                    throw new ProtocolViolationException("A started station needs a recipe.");
                }

                return new StationBlockState(input, output, recipe, started);
            default:
                throw new ProtocolViolationException($"Unknown or reserved block state kind {kind}.");
        }
    }

    /// <summary>A bed reference: construct, cell and expected block type (K6-30).</summary>
    public static void WriteBedRef(ByteWriter w, BedRef bed)
    {
        w.WriteVarUInt(bed.Construct.Value);
        WriteCellPos(w, bed.Cell);
        w.WriteVarUInt(bed.ExpectedType);
    }

    public static BedRef ReadBedRef(ref ByteReader r)
    {
        var construct = new EntityId(r.ReadVarUInt());
        var cell = ReadCellPos(ref r);
        ushort type = r.ReadVarUInt16();
        return new BedRef(construct, cell, type);
    }
}
