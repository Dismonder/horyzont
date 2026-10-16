using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Serialization;

/// <summary>
/// Binary codec for <see cref="Operation"/> frames: kind, format version, payload. A <see cref="CompoundOp"/> frame holds 1..4096
/// component frames (never another compound) and at most <see cref="ProtocolConstants.MaxCompoundBytes"/> bytes.
/// </summary>
public static class OperationCodec
{
    private const byte CurrentFormatVersion = 1;

    /// <summary>Appends the frame of <paramref name="op"/>.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="op">Operation.</param>
    public static void Write(ByteWriter w, Operation op)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(op);

        switch (op)
        {
            case TerrainEditOp te:
                if (te.Shape != TerrainShape.Sphere)
                {
                    throw new ArgumentException($"Invalid TerrainShape {(byte)te.Shape}.", nameof(op));
                }

                if (te.Mode < TerrainEditMode.Remove || te.Mode > TerrainEditMode.Add)
                {
                    throw new ArgumentException($"Invalid TerrainEditMode {(byte)te.Mode}.", nameof(op));
                }

                w.WriteByte((byte)OperationKind.TerrainEdit);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(te.Planet.Value);
                w.WriteByte((byte)te.Shape);
                w.WriteVector3(te.Center);
                w.WriteSingle(te.Radius);
                w.WriteByte((byte)te.Mode);
                w.WriteByte(te.MaterialId);
                break;

            case ConstructCreateOp cc:
                w.WriteByte((byte)OperationKind.ConstructCreate);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(cc.Construct.Value);
                CoreCodecPrimitives.WriteFrameRef(w, cc.Parent);
                w.WriteVector3(cc.Origin);
                w.WriteQuaternion(cc.Rotation);
                CoreCodecPrimitives.WriteCellPos(w, cc.FirstCell);
                CoreCodecPrimitives.WriteBlockState(w, cc.Block);
                break;

            case BlockSetOp bs:
                w.WriteByte((byte)OperationKind.BlockSet);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(bs.Construct.Value);
                CoreCodecPrimitives.WriteCellPos(w, bs.Cell);
                CoreCodecPrimitives.WriteBlockState(w, bs.Block);
                break;

            case BlockClearOp bc:
                w.WriteByte((byte)OperationKind.BlockClear);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(bc.Construct.Value);
                CoreCodecPrimitives.WriteCellPos(w, bc.Cell);
                break;

            case PlayerJoinOp pj:
                w.WriteByte((byte)OperationKind.PlayerJoin);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(pj.Player.Value);
                CoreCodecPrimitives.WritePlatformIdentity(w, pj.Identity);
                w.WriteString(pj.DisplayName, ProtocolConstants.MaxDisplayNameBytes);
                w.WriteVarUInt(pj.Entity.Value);
                CoreCodecPrimitives.WriteFrameRef(w, pj.Frame);
                w.WriteVarUInt(pj.ParentSeq);
                w.WriteVector3(pj.Position);
                w.WriteQuaternion(pj.BodyRotation);
                break;

            case PlayerLeaveOp pl:
                w.WriteByte((byte)OperationKind.PlayerLeave);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(pl.Player.Value);
                CoreCodecPrimitives.WriteFrameRef(w, pl.Frame);
                w.WriteVarUInt(pl.ParentSeq);
                w.WriteVector3(pl.Position);
                w.WriteQuaternion(pl.BodyRotation);
                w.WriteSingle(pl.Pitch);
                break;

            case CompoundOp compound:
                WriteCompound(w, compound);
                break;

            case ItemTransferOp it:
                w.WriteByte((byte)OperationKind.ItemTransfer);
                w.WriteByte(CurrentFormatVersion);
                CoreCodecPrimitives.WriteSlotRef(w, it.From);
                CoreCodecPrimitives.WriteSlotRef(w, it.To);
                w.WriteVarUInt(it.Type.Value);
                w.WriteVarUInt(it.Count);
                w.WriteVarUInt(it.ToStackLimit);
                break;

            case ItemSwapOp sw:
                w.WriteByte((byte)OperationKind.ItemSwap);
                w.WriteByte(CurrentFormatVersion);
                CoreCodecPrimitives.WriteSlotRef(w, sw.A);
                CoreCodecPrimitives.WriteSlotRef(w, sw.B);
                w.WriteVarUInt(sw.ExpectedA.Value);
                w.WriteVarUInt(sw.ExpectedB.Value);
                break;

            case ItemCreateOp ic:
                if (!Enum.IsDefined(ic.Source))
                {
                    throw new ArgumentException($"Invalid ItemSource {(byte)ic.Source}.", nameof(op));
                }

                w.WriteByte((byte)OperationKind.ItemCreate);
                w.WriteByte(CurrentFormatVersion);
                CoreCodecPrimitives.WriteSlotRef(w, ic.Slot);
                w.WriteVarUInt(ic.Type.Value);
                w.WriteVarUInt(ic.Count);
                w.WriteVarUInt(ic.StackLimit);
                w.WriteByte((byte)ic.Source);
                break;

            case ItemDestroyOp id:
                if (!Enum.IsDefined(id.Sink))
                {
                    throw new ArgumentException($"Invalid ItemSink {(byte)id.Sink}.", nameof(op));
                }

                w.WriteByte((byte)OperationKind.ItemDestroy);
                w.WriteByte(CurrentFormatVersion);
                CoreCodecPrimitives.WriteSlotRef(w, id.Slot);
                w.WriteVarUInt(id.Type.Value);
                w.WriteVarUInt(id.Count);
                w.WriteByte((byte)id.Sink);
                break;

            case ContainerCreateOp cr:
                if (!Enum.IsDefined(cr.ContainerKind) || !Enum.IsDefined(cr.Access) || !Enum.IsDefined(cr.Host.Kind))
                {
                    throw new ArgumentException("Invalid container kind, access or host kind.", nameof(op));
                }

                w.WriteByte((byte)OperationKind.ContainerCreate);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(cr.Id.Value);
                w.WriteByte((byte)cr.ContainerKind);
                CoreCodecPrimitives.WriteContainerHost(w, cr.Host);
                w.WriteVarUInt(cr.Owner.Value);
                w.WriteByte((byte)cr.Access);
                w.WriteVarUInt(cr.Size);
                break;

            case ContainerRemoveOp rm:
                w.WriteByte((byte)OperationKind.ContainerRemove);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(rm.Container.Value);
                break;

            case ContainerConfigureOp cf:
                if (!Enum.IsDefined(cf.Access) || cf.Size == 0 || cf.Size > ProtocolConstants.MaxContainerSlots)
                {
                    throw new ArgumentException("Invalid container access or size.", nameof(op));
                }

                w.WriteByte((byte)OperationKind.ContainerConfigure);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(cf.Container.Value);
                w.WriteVarUInt(cf.Size);
                w.WriteByte((byte)cf.Access);
                break;

            case BlockEntitySetOp be:
                w.WriteByte((byte)OperationKind.BlockEntitySet);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(be.Construct.Value);
                CoreCodecPrimitives.WriteCellPos(w, be.Cell);
                w.WriteVarUInt(be.ExpectedType);
                CoreCodecPrimitives.WriteBlockEntityState(w, be.State, allowNone: true);
                break;

            case RespawnPointSetOp rp:
                w.WriteByte((byte)OperationKind.RespawnPointSet);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(rp.Player.Value);
                if (rp.Bed is { } bed)
                {
                    w.WriteBool(true);
                    CoreCodecPrimitives.WriteBedRef(w, bed);
                }
                else
                {
                    w.WriteBool(false);
                }

                break;

            case BlockFunctionStateSetOp fs:
                if (fs.State >= BlockState.FunctionStateCount)
                {
                    throw new ArgumentException($"Function state {fs.State} is above {BlockState.FunctionStateCount - 1}.", nameof(op));
                }

                w.WriteByte((byte)OperationKind.BlockFunctionStateSet);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(fs.Construct.Value);
                CoreCodecPrimitives.WriteCellPos(w, fs.Cell);
                w.WriteVarUInt(fs.ExpectedType);
                w.WriteByte(fs.State);
                break;

            case EntityReparentOp er:
                w.WriteByte((byte)OperationKind.EntityReparent);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(er.Entity.Value);
                CoreCodecPrimitives.WriteFrameRef(w, er.NewParent);
                w.WriteVarUInt(er.NewParentSeq);
                w.WriteVector3(er.LocalPosition);
                w.WriteQuaternion(er.LocalRotation);
                w.WriteVector3(er.LocalVelocity);
                CoreCodecPrimitives.WriteOptionalCellPos(w, er.SeatCell);
                break;

            case ShipModeSetOp sm:
                if (sm.Mode < ConstructMode.Static || sm.Mode > ConstructMode.Ship)
                {
                    throw new ArgumentException($"Invalid ConstructMode {(byte)sm.Mode}.", nameof(op));
                }

                w.WriteByte((byte)OperationKind.ShipModeSet);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(sm.Construct.Value);
                w.WriteByte((byte)sm.Mode);
                CoreCodecPrimitives.WriteFrameRef(w, sm.Parent);
                w.WriteVarUInt(sm.ParentSeq);
                w.WriteVector3(sm.Origin);
                w.WriteQuaternion(sm.Rotation);
                break;

            case MarkerSetOp ms:
                w.WriteByte((byte)OperationKind.MarkerSet);
                w.WriteByte(CurrentFormatVersion);
                StateCodec.WriteMarker(w, ms.ToRecord());
                break;

            case MarkerRemoveOp mr:
                w.WriteByte((byte)OperationKind.MarkerRemove);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(mr.Marker.Value);
                break;

            case ShipFuelAddOp fa:
                if (!IsFuelAmount(fa.Units, fa.Capacity))
                {
                    throw new ArgumentException($"Invalid ShipFuelAddOp units {fa.Units} or capacity {fa.Capacity}.", nameof(op));
                }

                w.WriteByte((byte)OperationKind.ShipFuelAdd);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(fa.Construct.Value);
                w.WriteSingle(fa.Units);
                w.WriteSingle(fa.Capacity);
                break;

            default:
                throw new ArgumentException($"Unknown operation type {op.GetType().Name}.", nameof(op));
        }
    }

    /// <summary>Reads one operation frame.</summary>
    /// <param name="r">Reader positioned at the frame.</param>
    /// <returns>The operation.</returns>
    public static Operation Read(ref ByteReader r) => Read(ref r, allowCompound: true);

    private static void WriteCompound(ByteWriter w, CompoundOp compound)
    {
        var components = compound.Components ?? throw new ArgumentException("Compound operation without components.", nameof(compound));
        if (components.Count == 0 || components.Count > ProtocolConstants.MaxCompoundComponents)
        {
            throw new ArgumentException($"A compound operation has 1..{ProtocolConstants.MaxCompoundComponents} components, not {components.Count}.", nameof(compound));
        }

        var start = w.Length;
        w.WriteByte((byte)OperationKind.Compound);
        w.WriteByte(CurrentFormatVersion);
        w.WriteVarUInt((ulong)components.Count);
        foreach (var component in components)
        {
            if (component is null or CompoundOp)
            {
                throw new ArgumentException("A compound operation component is missing or is itself a compound.", nameof(compound));
            }

            Write(w, component);
            if (w.Length - start > ProtocolConstants.MaxCompoundBytes)
            {
                throw new ArgumentException($"A compound operation exceeds {ProtocolConstants.MaxCompoundBytes} bytes.", nameof(compound));
            }
        }
    }

    private static CompoundOp ReadCompound(ref ByteReader r, int start)
    {
        ulong count = r.ReadVarUInt();
        if (count == 0 || count > ProtocolConstants.MaxCompoundComponents)
        {
            throw new ProtocolViolationException($"A compound operation has 1..{ProtocolConstants.MaxCompoundComponents} components, not {count}.");
        }

        // Every component frame has at least a kind and a version byte.
        if ((ulong)r.Remaining < count * 2)
        {
            throw new ProtocolViolationException($"Declared {count} components exceed the remaining {r.Remaining} bytes.");
        }

        var components = new Operation[count];
        for (var i = 0; i < components.Length; i++)
        {
            components[i] = Read(ref r, allowCompound: false);
            if (r.Position - start > ProtocolConstants.MaxCompoundBytes)
            {
                throw new ProtocolViolationException($"A compound operation exceeds {ProtocolConstants.MaxCompoundBytes} bytes.");
            }
        }

        return new CompoundOp(components);
    }

    private static Operation Read(ref ByteReader r, bool allowCompound)
    {
        var start = r.Position;
        byte kindByte = r.ReadByte();
        var known = (kindByte >= (byte)OperationKind.TerrainEdit && kindByte <= (byte)OperationKind.EntityReparent)
            || (kindByte >= (byte)OperationKind.ShipModeSet && kindByte <= (byte)OperationKind.MarkerRemove)
            || (kindByte >= (byte)OperationKind.Compound && kindByte <= (byte)OperationKind.ContainerConfigure)
            || kindByte is (byte)OperationKind.BlockEntitySet or (byte)OperationKind.RespawnPointSet or (byte)OperationKind.BlockFunctionStateSet;
        if (!known)
        {
            throw new ProtocolViolationException($"Unknown operation kind {kindByte}.");
        }

        if (kindByte == (byte)OperationKind.Compound && !allowCompound)
        {
            throw new ProtocolViolationException("A compound operation cannot contain another compound operation.");
        }

        byte version = r.ReadByte();
        if (version != CurrentFormatVersion)
        {
            throw new ProtocolViolationException($"Unsupported operation format version {version}.");
        }

        var kind = (OperationKind)kindByte;
        switch (kind)
        {
            case OperationKind.Compound:
                return ReadCompound(ref r, start);

            case OperationKind.ItemTransfer:
                var itFrom = CoreCodecPrimitives.ReadSlotRef(ref r);
                var itTo = CoreCodecPrimitives.ReadSlotRef(ref r);
                var itType = new ItemTypeId(r.ReadVarUInt16());
                ushort itCount = r.ReadVarUInt16();
                ushort itLimit = r.ReadVarUInt16();
                return new ItemTransferOp(itFrom, itTo, itType, itCount, itLimit);

            case OperationKind.ItemSwap:
                var swA = CoreCodecPrimitives.ReadSlotRef(ref r);
                var swB = CoreCodecPrimitives.ReadSlotRef(ref r);
                var swExpectedA = new ItemTypeId(r.ReadVarUInt16());
                var swExpectedB = new ItemTypeId(r.ReadVarUInt16());
                return new ItemSwapOp(swA, swB, swExpectedA, swExpectedB);

            case OperationKind.ItemCreate:
                var icSlot = CoreCodecPrimitives.ReadSlotRef(ref r);
                var icType = new ItemTypeId(r.ReadVarUInt16());
                ushort icCount = r.ReadVarUInt16();
                ushort icLimit = r.ReadVarUInt16();
                var icSource = (ItemSource)CoreCodecPrimitives.ReadEnumByte(ref r, (byte)ItemSource.Harvest, (byte)ItemSource.Starter, "item source");
                return new ItemCreateOp(icSlot, icType, icCount, icLimit, icSource);

            case OperationKind.ItemDestroy:
                var idSlot = CoreCodecPrimitives.ReadSlotRef(ref r);
                var idType = new ItemTypeId(r.ReadVarUInt16());
                ushort idCount = r.ReadVarUInt16();
                var idSink = (ItemSink)CoreCodecPrimitives.ReadEnumByte(ref r, (byte)ItemSink.Craft, (byte)ItemSink.Offering, "item sink");
                return new ItemDestroyOp(idSlot, idType, idCount, idSink);

            case OperationKind.ContainerCreate:
                var crId = new ContainerId(r.ReadVarUInt());
                var crKind = (ContainerKind)CoreCodecPrimitives.ReadEnumByte(
                    ref r, (byte)ContainerKind.PlayerBag, (byte)ContainerKind.DroppedItem, "container kind");
                var crHost = CoreCodecPrimitives.ReadContainerHost(ref r);
                var crOwner = new PlayerId(r.ReadVarUInt());
                var crAccess = (ContainerAccess)CoreCodecPrimitives.ReadEnumByte(
                    ref r, (byte)ContainerAccess.Shared, (byte)ContainerAccess.Private, "container access");
                ushort crSize = r.ReadVarUInt16();
                return new ContainerCreateOp(crId, crKind, crHost, crOwner, crAccess, crSize);

            case OperationKind.ContainerRemove:
                return new ContainerRemoveOp(new ContainerId(r.ReadVarUInt()));

            case OperationKind.ContainerConfigure:
                var cfId = new ContainerId(r.ReadVarUInt());
                ushort cfSize = r.ReadVarUInt16();
                if (cfSize == 0 || cfSize > ProtocolConstants.MaxContainerSlots)
                {
                    throw new ProtocolViolationException($"Container size {cfSize} must be in 1..{ProtocolConstants.MaxContainerSlots}.");
                }

                var cfAccess = (ContainerAccess)CoreCodecPrimitives.ReadEnumByte(
                    ref r, (byte)ContainerAccess.Shared, (byte)ContainerAccess.Private, "container access");
                return new ContainerConfigureOp(cfId, cfSize, cfAccess);

            case OperationKind.BlockEntitySet:
                var beConstruct = new EntityId(r.ReadVarUInt());
                var beCell = CoreCodecPrimitives.ReadCellPos(ref r);
                ushort beType = r.ReadVarUInt16();
                var beState = CoreCodecPrimitives.ReadBlockEntityState(ref r, allowNone: true);
                return new BlockEntitySetOp(beConstruct, beCell, beType, beState);

            case OperationKind.RespawnPointSet:
                var rpPlayer = new PlayerId(r.ReadVarUInt());
                BedRef? rpBed = r.ReadBool() ? CoreCodecPrimitives.ReadBedRef(ref r) : null;
                return new RespawnPointSetOp(rpPlayer, rpBed);

            case OperationKind.BlockFunctionStateSet:
                var fsConstruct = new EntityId(r.ReadVarUInt());
                var fsCell = CoreCodecPrimitives.ReadCellPos(ref r);
                ushort fsType = r.ReadVarUInt16();
                byte fsState = r.ReadByte();
                if (fsState >= BlockState.FunctionStateCount)
                {
                    throw new ProtocolViolationException($"Function state {fsState} is above {BlockState.FunctionStateCount - 1}.");
                }

                return new BlockFunctionStateSetOp(fsConstruct, fsCell, fsType, fsState);

            case OperationKind.TerrainEdit:
                var planet = new PlanetId(r.ReadVarUInt());
                byte shapeByte = r.ReadByte();
                if (shapeByte != (byte)TerrainShape.Sphere)
                {
                    throw new ProtocolViolationException($"Unknown terrain shape {shapeByte}.");
                }

                var center = r.ReadVector3();
                float radius = r.ReadSingle();
                byte modeByte = r.ReadByte();
                if (modeByte < (byte)TerrainEditMode.Remove || modeByte > (byte)TerrainEditMode.Add)
                {
                    throw new ProtocolViolationException($"Unknown terrain edit mode {modeByte}.");
                }

                byte materialId = r.ReadByte();
                return new TerrainEditOp(planet, (TerrainShape)shapeByte, center, radius, (TerrainEditMode)modeByte, materialId);

            case OperationKind.ConstructCreate:
                var ccId = new EntityId(r.ReadVarUInt());
                var ccParent = CoreCodecPrimitives.ReadFrameRef(ref r);
                var ccOrigin = r.ReadVector3();
                var ccRotation = r.ReadQuaternion();
                var ccCell = CoreCodecPrimitives.ReadCellPos(ref r);
                var ccBlock = CoreCodecPrimitives.ReadBlockState(ref r);
                return new ConstructCreateOp(ccId, ccParent, ccOrigin, ccRotation, ccCell, ccBlock);

            case OperationKind.BlockSet:
                var bsConstruct = new EntityId(r.ReadVarUInt());
                var bsCell = CoreCodecPrimitives.ReadCellPos(ref r);
                var bsBlock = CoreCodecPrimitives.ReadBlockState(ref r);
                return new BlockSetOp(bsConstruct, bsCell, bsBlock);

            case OperationKind.BlockClear:
                var bcConstruct = new EntityId(r.ReadVarUInt());
                var bcCell = CoreCodecPrimitives.ReadCellPos(ref r);
                return new BlockClearOp(bcConstruct, bcCell);

            case OperationKind.PlayerJoin:
                var pjPlayer = new PlayerId(r.ReadVarUInt());
                var pjIdentity = CoreCodecPrimitives.ReadPlatformIdentity(ref r);
                string pjName = r.ReadString(ProtocolConstants.MaxDisplayNameBytes);
                var pjEntity = new EntityId(r.ReadVarUInt());
                var pjFrame = CoreCodecPrimitives.ReadFrameRef(ref r);
                uint pjParentSeq = r.ReadVarUInt32();
                var pjPosition = r.ReadVector3();
                var pjRotation = r.ReadQuaternion();
                return new PlayerJoinOp(pjPlayer, pjIdentity, pjName, pjEntity, pjFrame, pjParentSeq, pjPosition, pjRotation);

            case OperationKind.PlayerLeave:
                var plPlayer = new PlayerId(r.ReadVarUInt());
                var plFrame = CoreCodecPrimitives.ReadFrameRef(ref r);
                uint plParentSeq = r.ReadVarUInt32();
                var plPosition = r.ReadVector3();
                var plRotation = r.ReadQuaternion();
                float plPitch = r.ReadSingle();
                return new PlayerLeaveOp(plPlayer, plFrame, plParentSeq, plPosition, plRotation, plPitch);

            case OperationKind.EntityReparent:
                var erEntity = new EntityId(r.ReadVarUInt());
                var erParent = CoreCodecPrimitives.ReadFrameRef(ref r);
                uint erParentSeq = r.ReadVarUInt32();
                var erPosition = r.ReadVector3();
                var erRotation = r.ReadQuaternion();
                var erVelocity = r.ReadVector3();
                var erSeat = CoreCodecPrimitives.ReadOptionalCellPos(ref r);
                return new EntityReparentOp(erEntity, erParent, erParentSeq, erPosition, erRotation, erVelocity, erSeat);

            case OperationKind.ShipModeSet:
                var smConstruct = new EntityId(r.ReadVarUInt());
                byte smModeByte = r.ReadByte();
                if (smModeByte > (byte)ConstructMode.Ship)
                {
                    throw new ProtocolViolationException($"Unknown construct mode {smModeByte}.");
                }

                var smParent = CoreCodecPrimitives.ReadFrameRef(ref r);
                uint smParentSeq = r.ReadVarUInt32();
                var smOrigin = r.ReadVector3();
                var smRotation = r.ReadQuaternion();
                return new ShipModeSetOp(smConstruct, (ConstructMode)smModeByte, smParent, smParentSeq, smOrigin, smRotation);

            case OperationKind.MarkerSet:
                var marker = StateCodec.ReadMarker(ref r);
                return new MarkerSetOp(marker.Id, marker.Owner, marker.Planet, marker.Position, marker.Name, marker.Icon, marker.Color);

            case OperationKind.MarkerRemove:
                var removed = new MarkerId(r.ReadVarUInt());
                if (removed.IsNone || removed.Value > MarkerIdAllocator.MaxValue)
                {
                    throw new ProtocolViolationException("Invalid marker.");
                }

                return new MarkerRemoveOp(removed);

            case OperationKind.ShipFuelAdd:
                var faConstruct = new EntityId(r.ReadVarUInt());
                float faUnits = r.ReadSingle();
                float faCapacity = r.ReadSingle();
                if (!IsFuelAmount(faUnits, faCapacity))
                {
                    throw new ProtocolViolationException($"Invalid fuel units {faUnits} or capacity {faCapacity}.");
                }

                return new ShipFuelAddOp(faConstruct, faUnits, faCapacity);

            default:
                throw new ProtocolViolationException($"Unhandled operation kind {kind}.");
        }
    }

    /// <summary>Units finite and positive, capacity finite and non-negative (K5-24 decision 13).</summary>
    private static bool IsFuelAmount(float units, float capacity) =>
        float.IsFinite(units) && units > 0f && float.IsFinite(capacity) && capacity >= 0f;
}
