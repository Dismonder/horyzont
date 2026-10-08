using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

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
        if (codeByte < (byte)IntentCode.TerrainTool || codeByte > (byte)IntentCode.RemoveBlock)
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

            default:
                throw new ProtocolViolationException($"Unhandled intent code {code}.");
        }
    }
}
