using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Serialization;

/// <summary>Binary codec for <see cref="Operation"/> frames: kind, format version, payload.</summary>
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

            default:
                throw new ArgumentException($"Unknown operation type {op.GetType().Name}.", nameof(op));
        }
    }

    /// <summary>Reads one operation frame.</summary>
    /// <param name="r">Reader positioned at the frame.</param>
    /// <returns>The operation.</returns>
    public static Operation Read(ref ByteReader r)
    {
        byte kindByte = r.ReadByte();
        if (kindByte is < (byte)OperationKind.TerrainEdit or > (byte)OperationKind.EntityReparent and not (byte)OperationKind.ShipModeSet)
        {
            throw new ProtocolViolationException($"Unknown operation kind {kindByte}.");
        }

        byte version = r.ReadByte();
        if (version != CurrentFormatVersion)
        {
            throw new ProtocolViolationException($"Unsupported operation format version {version}.");
        }

        var kind = (OperationKind)kindByte;
        switch (kind)
        {
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

            default:
                throw new ProtocolViolationException($"Unhandled operation kind {kind}.");
        }
    }
}
