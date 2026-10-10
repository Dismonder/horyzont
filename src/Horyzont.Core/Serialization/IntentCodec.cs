using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Serialization;

/// <summary>Binary codec for <see cref="Intent"/> frames: intent code, format version, payload.</summary>
public static class IntentCodec
{
    private const byte CurrentFormatVersion = 1;

    private enum IntentCode : byte
    {
        TerrainTool = 1,
        PlaceBlock = 2,
        RemoveBlock = 3,
        ShipMode = 10,
        MarkerSet = 14,
        MarkerRemove = 15,
        Refuel = 13,
        ItemMove = 20,
        ItemDrop = 21,
    }

    private enum ItemMoveTargetCode : byte
    {
        Slot = 1,
        AnySlot = 2,
    }

    private enum BlockTargetCode : byte
    {
        ConstructFace = 1,
        Terrain = 2,
    }

    /// <summary>Appends the frame of <paramref name="intent"/>.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="intent">Intent.</param>
    public static void Write(ByteWriter w, Intent intent)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(intent);

        switch (intent)
        {
            case TerrainToolIntent tt:
                if (tt.Mode < TerrainEditMode.Remove || tt.Mode > TerrainEditMode.Add)
                {
                    throw new ArgumentException($"Invalid TerrainEditMode {(byte)tt.Mode}.", nameof(intent));
                }

                w.WriteByte((byte)IntentCode.TerrainTool);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(tt.Planet.Value);
                w.WriteVector3(tt.RayOrigin);
                w.WriteVector3(tt.RayDirection);
                w.WriteSingle(tt.Radius);
                w.WriteByte((byte)tt.Mode);
                w.WriteByte(tt.MaterialId);
                break;

            case PlaceBlockIntent pb:
                w.WriteByte((byte)IntentCode.PlaceBlock);
                w.WriteByte(CurrentFormatVersion);
                switch (pb.Target)
                {
                    case ConstructFaceTarget cft:
                        if (cft.Face < Face.PosX || cft.Face > Face.NegZ)
                        {
                            throw new ArgumentException($"Invalid Face {(byte)cft.Face}.", nameof(intent));
                        }

                        w.WriteByte((byte)BlockTargetCode.ConstructFace);
                        w.WriteVarUInt(cft.Construct.Value);
                        CoreCodecPrimitives.WriteCellPos(w, cft.Cell);
                        w.WriteByte((byte)cft.Face);
                        break;

                    case TerrainTarget ttTarget:
                        w.WriteByte((byte)BlockTargetCode.Terrain);
                        w.WriteVarUInt(ttTarget.Planet.Value);
                        w.WriteVector3(ttTarget.RayOrigin);
                        w.WriteVector3(ttTarget.RayDirection);
                        w.WriteVector3(ttTarget.LookForward);
                        break;

                    default:
                        throw new ArgumentException($"Unknown BlockTarget type {pb.Target?.GetType().Name}.", nameof(intent));
                }

                w.WriteVarUInt(pb.BlockTypeId);
                w.WriteByte(pb.Orientation);
                break;

            case RemoveBlockIntent rb:
                w.WriteByte((byte)IntentCode.RemoveBlock);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(rb.Construct.Value);
                CoreCodecPrimitives.WriteCellPos(w, rb.Cell);
                break;

            case ShipModeIntent sm:
                if (sm.TargetMode < ConstructMode.Static || sm.TargetMode > ConstructMode.Ship)
                {
                    throw new ArgumentException($"Invalid ConstructMode {(byte)sm.TargetMode}.", nameof(intent));
                }

                w.WriteByte((byte)IntentCode.ShipMode);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(sm.Construct.Value);
                CoreCodecPrimitives.WriteCellPos(w, sm.CockpitCell);
                w.WriteByte((byte)sm.TargetMode);
                w.WriteVector3(sm.RayOrigin);
                w.WriteVector3(sm.RayDirection);
                break;

            case MarkerSetIntent ms:
                if (!MarkerRules.IsValidStyle(ms.Icon, ms.Color))
                {
                    throw new ArgumentException("Invalid marker icon or colour.", nameof(intent));
                }

                w.WriteByte((byte)IntentCode.MarkerSet);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(ms.Marker.Value);
                w.WriteBool(ms.Shared);
                w.WriteVarUInt(ms.Planet.Value);
                w.WriteVector3(ms.Position);
                w.WriteString(ms.Name, MarkerRules.MaxNameBytes);
                w.WriteByte((byte)ms.Icon);
                w.WriteByte((byte)ms.Color);
                break;

            case MarkerRemoveIntent mr:
                w.WriteByte((byte)IntentCode.MarkerRemove);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(mr.Marker.Value);
                break;

            case RefuelIntent rf:
                w.WriteByte((byte)IntentCode.Refuel);
                w.WriteByte(CurrentFormatVersion);
                w.WriteVarUInt(rf.Ship.Value);
                w.WriteVarUInt(rf.Station.Value);
                CoreCodecPrimitives.WriteCellPos(w, rf.StationCell);
                w.WriteVector3(rf.RayOrigin);
                w.WriteVector3(rf.RayDirection);
                break;

            case ItemMoveIntent move:
                if (move.To.AnySlot && move.To.Slot != 0)
                {
                    throw new ArgumentException("An any-slot target has slot 0.", nameof(intent));
                }

                w.WriteByte((byte)IntentCode.ItemMove);
                w.WriteByte(CurrentFormatVersion);
                CoreCodecPrimitives.WriteSlotRef(w, move.From);
                w.WriteByte((byte)(move.To.AnySlot ? ItemMoveTargetCode.AnySlot : ItemMoveTargetCode.Slot));
                w.WriteVarUInt(move.To.Container.Value);
                if (!move.To.AnySlot)
                {
                    w.WriteVarUInt(move.To.Slot);
                }

                w.WriteVarUInt(move.Count);
                w.WriteVarUInt(move.ExpectedType.Value);
                w.WriteVarUInt(move.ExpectedCount);
                break;

            case ItemDropIntent drop:
                w.WriteByte((byte)IntentCode.ItemDrop);
                w.WriteByte(CurrentFormatVersion);
                CoreCodecPrimitives.WriteSlotRef(w, drop.Slot);
                w.WriteVarUInt(drop.Count);
                w.WriteVarUInt(drop.ExpectedType.Value);
                w.WriteVarUInt(drop.ExpectedCount);
                break;

            default:
                throw new ArgumentException($"Unknown intent type {intent.GetType().Name}.", nameof(intent));
        }
    }

    /// <summary>Reads one intent frame.</summary>
    /// <param name="r">Reader positioned at the frame.</param>
    /// <returns>The intent.</returns>
    public static Intent Read(ref ByteReader r)
    {
        byte codeByte = r.ReadByte();
        if (codeByte is < (byte)IntentCode.TerrainTool or > (byte)IntentCode.RemoveBlock and not ((byte)IntentCode.ShipMode or (byte)IntentCode.MarkerSet or (byte)IntentCode.MarkerRemove or (byte)IntentCode.Refuel or (byte)IntentCode.ItemMove or (byte)IntentCode.ItemDrop))
        {
            throw new ProtocolViolationException($"Unknown intent code {codeByte}.");
        }

        byte version = r.ReadByte();
        if (version != CurrentFormatVersion)
        {
            throw new ProtocolViolationException($"Unsupported intent format version {version}.");
        }

        var code = (IntentCode)codeByte;
        switch (code)
        {
            case IntentCode.TerrainTool:
                var planet = new PlanetId(r.ReadVarUInt());
                var rayOrigin = r.ReadVector3();
                var rayDir = r.ReadVector3();
                float radius = r.ReadSingle();
                byte modeByte = r.ReadByte();
                if (modeByte < (byte)TerrainEditMode.Remove || modeByte > (byte)TerrainEditMode.Add)
                {
                    throw new ProtocolViolationException($"Unknown terrain edit mode {modeByte}.");
                }

                byte materialId = r.ReadByte();
                return new TerrainToolIntent(planet, rayOrigin, rayDir, radius, (TerrainEditMode)modeByte, materialId);

            case IntentCode.PlaceBlock:
                byte targetCodeByte = r.ReadByte();
                BlockTarget target;
                if (targetCodeByte == (byte)BlockTargetCode.ConstructFace)
                {
                    var construct = new EntityId(r.ReadVarUInt());
                    var cell = CoreCodecPrimitives.ReadCellPos(ref r);
                    byte faceByte = r.ReadByte();
                    if (faceByte > (byte)Face.NegZ)
                    {
                        throw new ProtocolViolationException($"Unknown face {faceByte}.");
                    }

                    target = new ConstructFaceTarget(construct, cell, (Face)faceByte);
                }
                else if (targetCodeByte == (byte)BlockTargetCode.Terrain)
                {
                    var ttPlanet = new PlanetId(r.ReadVarUInt());
                    var origin = r.ReadVector3();
                    var dir = r.ReadVector3();
                    var look = r.ReadVector3();
                    target = new TerrainTarget(ttPlanet, origin, dir, look);
                }
                else
                {
                    throw new ProtocolViolationException($"Unknown block target code {targetCodeByte}.");
                }

                ushort blockTypeId = r.ReadVarUInt16();
                byte orientation = r.ReadByte();
                return new PlaceBlockIntent(target, blockTypeId, orientation);

            case IntentCode.RemoveBlock:
                var rbConstruct = new EntityId(r.ReadVarUInt());
                var rbCell = CoreCodecPrimitives.ReadCellPos(ref r);
                return new RemoveBlockIntent(rbConstruct, rbCell);

            case IntentCode.ShipMode:
                var smConstruct = new EntityId(r.ReadVarUInt());
                var smCell = CoreCodecPrimitives.ReadCellPos(ref r);
                byte smModeByte = r.ReadByte();
                if (smModeByte > (byte)ConstructMode.Ship)
                {
                    throw new ProtocolViolationException($"Unknown construct mode {smModeByte}.");
                }

                var smOrigin = r.ReadVector3();
                var smDirection = r.ReadVector3();
                return new ShipModeIntent(smConstruct, smCell, (ConstructMode)smModeByte, smOrigin, smDirection);

            case IntentCode.MarkerSet:
                var msMarker = new MarkerId(r.ReadVarUInt());
                bool msShared = r.ReadBool();
                var msPlanet = new PlanetId(r.ReadVarUInt());
                var msPosition = r.ReadVector3();
                string msName = r.ReadString(MarkerRules.MaxNameBytes);
                byte msIcon = r.ReadByte();
                byte msColor = r.ReadByte();
                if (msIcon > (byte)MarkerIcon.Poi || msColor > (byte)MarkerColor.Orange || msMarker.Value > MarkerIdAllocator.MaxValue)
                {
                    throw new ProtocolViolationException("Invalid marker intent.");
                }

                return new MarkerSetIntent(msMarker, msShared, msPlanet, msPosition, msName, (MarkerIcon)msIcon, (MarkerColor)msColor);

            case IntentCode.MarkerRemove:
                var mrMarker = new MarkerId(r.ReadVarUInt());
                if (mrMarker.IsNone || mrMarker.Value > MarkerIdAllocator.MaxValue)
                {
                    throw new ProtocolViolationException("Invalid marker intent.");
                }

                return new MarkerRemoveIntent(mrMarker);

            case IntentCode.Refuel:
                var rfShip = new EntityId(r.ReadVarUInt());
                var rfStation = new EntityId(r.ReadVarUInt());
                var rfCell = CoreCodecPrimitives.ReadCellPos(ref r);
                var rfOrigin = r.ReadVector3();
                var rfDirection = r.ReadVector3();
                return new RefuelIntent(rfShip, rfStation, rfCell, rfOrigin, rfDirection);

            case IntentCode.ItemMove:
                var from = CoreCodecPrimitives.ReadSlotRef(ref r);
                var targetCode = (ItemMoveTargetCode)CoreCodecPrimitives.ReadEnumByte(
                    ref r, (byte)ItemMoveTargetCode.Slot, (byte)ItemMoveTargetCode.AnySlot, "item move target");
                var to = targetCode == ItemMoveTargetCode.Slot
                    ? ItemMoveTarget.ToSlot(CoreCodecPrimitives.ReadSlotRef(ref r))
                    : ItemMoveTarget.AnyIn(new ContainerId(r.ReadVarUInt()));
                ushort count = r.ReadVarUInt16();
                var expectedType = new ItemTypeId(r.ReadVarUInt16());
                ushort expectedCount = r.ReadVarUInt16();
                return new ItemMoveIntent(from, to, count, expectedType, expectedCount);

            case IntentCode.ItemDrop:
                var slot = CoreCodecPrimitives.ReadSlotRef(ref r);
                ushort dropCount = r.ReadVarUInt16();
                var dropType = new ItemTypeId(r.ReadVarUInt16());
                ushort dropExpected = r.ReadVarUInt16();
                return new ItemDropIntent(slot, dropCount, dropType, dropExpected);

            default:
                throw new ProtocolViolationException($"Unhandled intent code {code}.");
        }
    }
}
