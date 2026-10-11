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
        if (codeByte is < (byte)IntentCode.TerrainTool or > (byte)IntentCode.RemoveBlock and not (byte)IntentCode.ShipMode and not (byte)IntentCode.MarkerSet and not (byte)IntentCode.MarkerRemove)
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
                return new MarkerRemoveIntent(new MarkerId(r.ReadVarUInt()));

            default:
                throw new ProtocolViolationException($"Unhandled intent code {code}.");
        }
    }
}
