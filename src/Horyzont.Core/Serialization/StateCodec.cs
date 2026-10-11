using System.Buffers.Binary;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Serialization;

/// <summary>Binary codec for checkpoint records, registry entries, chunk blocks and SQLite blobs.</summary>
public static class StateCodec
{
    private const byte CurrentFormatVersion = 1;
    private const int ChunkCellCount = 4096;
    private const int TerrainBlockHeaderBytes = 7;

    /// <summary>Writes a construct header record without a prefix.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="h">Record.</param>
    public static void WriteConstructHeader(ByteWriter w, ConstructHeaderRecord h)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(h);

        if (h.Mode < ConstructMode.Static || h.Mode > ConstructMode.Ship)
        {
            throw new ArgumentException($"Invalid ConstructMode {(byte)h.Mode}.", nameof(h));
        }

        w.WriteVarUInt(h.Id.Value);
        CoreCodecPrimitives.WriteFrameRef(w, h.Parent);
        w.WriteVarUInt(h.ParentSeq);
        w.WriteByte((byte)h.Mode);
        w.WriteVector3(h.Origin);
        w.WriteQuaternion(h.Rotation);
        w.WriteVarUInt(h.Version);
    }

    /// <summary>Reads a construct header record.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>The record.</returns>
    public static ConstructHeaderRecord ReadConstructHeader(ref ByteReader r)
    {
        var id = new EntityId(r.ReadVarUInt());
        var parent = CoreCodecPrimitives.ReadFrameRef(ref r);
        uint parentSeq = r.ReadVarUInt32();
        byte modeByte = r.ReadByte();
        if (modeByte > (byte)ConstructMode.Ship)
        {
            throw new ProtocolViolationException($"Unknown construct mode {modeByte}.");
        }

        var origin = r.ReadVector3();
        var rotation = r.ReadQuaternion();
        uint version = r.ReadVarUInt32();

        return new ConstructHeaderRecord(id, parent, parentSeq, (ConstructMode)modeByte, origin, rotation, version);
    }

    /// <summary>Writes a player record without a prefix.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="p">Record.</param>
    public static void WritePlayer(ByteWriter w, PlayerRecord p)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(p);

        w.WriteVarUInt(p.Player.Value);
        CoreCodecPrimitives.WritePlatformIdentity(w, p.Identity);
        w.WriteString(p.DisplayName, ProtocolConstants.MaxDisplayNameBytes);
        w.WriteVarUInt(p.Entity.Value);
        CoreCodecPrimitives.WriteFrameRef(w, p.Frame);
        w.WriteVarUInt(p.ParentSeq);
        CoreCodecPrimitives.WriteOptionalCellPos(w, p.Seat);
        w.WriteVector3(p.Position);
        w.WriteQuaternion(p.BodyRotation);
        w.WriteSingle(p.Pitch);
        w.WriteBool(p.IsOnline);
    }

    /// <summary>Reads a player record.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>The record.</returns>
    public static PlayerRecord ReadPlayer(ref ByteReader r)
    {
        var player = new PlayerId(r.ReadVarUInt());
        var identity = CoreCodecPrimitives.ReadPlatformIdentity(ref r);
        string displayName = r.ReadString(ProtocolConstants.MaxDisplayNameBytes);
        var entity = new EntityId(r.ReadVarUInt());
        var frame = CoreCodecPrimitives.ReadFrameRef(ref r);
        uint parentSeq = r.ReadVarUInt32();
        var seat = CoreCodecPrimitives.ReadOptionalCellPos(ref r);
        var position = r.ReadVector3();
        var bodyRotation = r.ReadQuaternion();
        float pitch = r.ReadSingle();
        bool isOnline = r.ReadBool();

        return new PlayerRecord(player, identity, displayName, entity, frame, parentSeq, seat, position, bodyRotation, pitch, isOnline);
    }

    /// <summary>Writes planet parameters without a prefix.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="p">Record.</param>
    public static void WritePlanet(ByteWriter w, PlanetParams p)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(p);

        w.WriteVarUInt(p.Id.Value);
        w.WriteVarUInt(p.System.Value);
        w.WriteUInt64(p.Seed);
        w.WriteString(p.GeneratorProfile, ProtocolConstants.MaxRegistryKeyBytes);
        w.WriteSingle(p.RadiusMeters);
        w.WriteSingle(p.VoxelSizeMeters);
        w.WriteSingle(p.Gravity);
        w.WriteSingle(p.AtmosphereHeightMeters);
        w.WriteSingle(p.CoreDepthMeters);
        w.WriteSingle(p.DayLengthSeconds);
    }

    /// <summary>Reads planet parameters.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>The record.</returns>
    public static PlanetParams ReadPlanet(ref ByteReader r)
    {
        var id = new PlanetId(r.ReadVarUInt());
        var system = new SystemId(r.ReadVarUInt());
        ulong seed = r.ReadUInt64();
        string profile = r.ReadString(ProtocolConstants.MaxRegistryKeyBytes);
        float radius = r.ReadSingle();
        float voxel = r.ReadSingle();
        float gravity = r.ReadSingle();
        float atmosphere = r.ReadSingle();
        float core = r.ReadSingle();
        float day = r.ReadSingle();

        return new PlanetParams(id, system, seed, profile, radius, voxel, gravity, atmosphere, core, day);
    }

    /// <summary>Writes a registry entry; the id must be in 1..<paramref name="maxId"/>.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="e">Entry.</param>
    /// <param name="maxId">Largest allowed id (2048 for block types, 255 for materials).</param>
    public static void WriteRegistryEntry(ByteWriter w, RegistryEntry e, int maxId)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (e.Id < 1 || e.Id > maxId)
        {
            throw new ArgumentException($"Registry entry id {e.Id} must be in 1..{maxId}.", nameof(e));
        }

        w.WriteVarUInt(e.Id);
        w.WriteString(e.Key, ProtocolConstants.MaxRegistryKeyBytes);
    }

    /// <summary>Reads a registry entry.</summary>
    /// <param name="r">Reader.</param>
    /// <param name="maxId">Largest allowed id.</param>
    /// <returns>The entry.</returns>
    public static RegistryEntry ReadRegistryEntry(ref ByteReader r, int maxId)
    {
        ulong idUlong = r.ReadVarUInt();
        if (idUlong < 1 || idUlong > (ulong)maxId)
        {
            throw new ProtocolViolationException($"Registry entry id {idUlong} must be in 1..{maxId}.");
        }

        string key = r.ReadString(ProtocolConstants.MaxRegistryKeyBytes);
        return new RegistryEntry((ushort)idUlong, key);
    }

    /// <summary>Writes the 4096 blocks of a chunk as canonical run-length encoding.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="blocks">Exactly 4096 packed block states.</param>
    public static void WriteChunkBlocks(ByteWriter w, ReadOnlySpan<uint> blocks)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (blocks.Length != ChunkCellCount)
        {
            throw new ArgumentException($"Blocks span must contain exactly {ChunkCellCount} elements (got {blocks.Length}).", nameof(blocks));
        }

        // Validate all block states first and count maximal runs
        uint currentVal = blocks[0];
        if (!new BlockState(currentVal).IsWellFormed)
        {
            throw new ArgumentException("Block state is not well-formed.", nameof(blocks));
        }

        int runCount = 1;
        for (int i = 1; i < blocks.Length; i++)
        {
            uint val = blocks[i];
            if (!new BlockState(val).IsWellFormed)
            {
                throw new ArgumentException("Block state is not well-formed.", nameof(blocks));
            }

            if (val != currentVal)
            {
                runCount++;
                currentVal = val;
            }
        }

        w.WriteVarUInt((ulong)runCount);

        int runLength = 1;
        currentVal = blocks[0];
        for (int i = 1; i < blocks.Length; i++)
        {
            uint val = blocks[i];
            if (val == currentVal)
            {
                runLength++;
            }
            else
            {
                w.WriteVarUInt((ulong)runLength);
                w.WriteUInt32(currentVal);
                currentVal = val;
                runLength = 1;
            }
        }

        w.WriteVarUInt((ulong)runLength);
        w.WriteUInt32(currentVal);
    }

    /// <summary>Reads run-length encoded chunk blocks.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>A new array of 4096 packed block states.</returns>
    public static uint[] ReadChunkBlocks(ref ByteReader r)
    {
        ulong runCountUlong = r.ReadVarUInt();
        if (runCountUlong < 1 || runCountUlong > ChunkCellCount)
        {
            throw new ProtocolViolationException($"Run count {runCountUlong} must be 1..{ChunkCellCount}.");
        }

        int runCount = (int)runCountUlong;
        // Each run takes at least 1 byte (length) + 4 bytes (value) = 5 bytes
        if (r.Remaining < runCount * 5)
        {
            throw new ProtocolViolationException($"Declared {runCount} runs exceed remaining data of {r.Remaining} bytes.");
        }

        var blocks = new uint[ChunkCellCount];
        int totalLength = 0;
        uint? previousVal = null;

        for (int i = 0; i < runCount; i++)
        {
            ulong lenUlong = r.ReadVarUInt();
            if (lenUlong < 1 || lenUlong > (ulong)(ChunkCellCount - totalLength))
            {
                throw new ProtocolViolationException($"Run length {lenUlong} out of bounds.");
            }

            int len = (int)lenUlong;
            uint val = r.ReadUInt32();
            if (!new BlockState(val).IsWellFormed)
            {
                throw new ProtocolViolationException("Malformed block state in chunk RLE.");
            }

            if (previousVal.HasValue && val == previousVal.Value)
            {
                throw new ProtocolViolationException("Adjacent runs with identical value in chunk RLE.");
            }

            previousVal = val;
            blocks.AsSpan(totalLength, len).Fill(val);
            totalLength += len;
        }

        if (totalLength != ChunkCellCount)
        {
            throw new ProtocolViolationException($"Sum of run lengths is {totalLength}, expected {ChunkCellCount}.");
        }

        return blocks;
    }

    /// <summary>Writes a chunk position followed by its run-length encoded blocks.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="c">Chunk record.</param>
    public static void WriteChunk(ByteWriter w, ConstructChunkRecord c)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(c);

        CoreCodecPrimitives.WriteChunkPos(w, c.Key.Chunk);
        WriteChunkBlocks(w, c.Blocks);
    }

    /// <summary>Reads a chunk position and blocks; the key gets <paramref name="construct"/>.</summary>
    /// <param name="r">Reader.</param>
    /// <param name="construct">Owning construct.</param>
    /// <returns>The chunk record.</returns>
    public static ConstructChunkRecord ReadChunk(ref ByteReader r, EntityId construct)
    {
        var chunkPos = CoreCodecPrimitives.ReadChunkPos(ref r);
        var blocks = ReadChunkBlocks(ref r);
        return new ConstructChunkRecord(new ConstructChunkKey(construct, chunkPos), blocks);
    }

    /// <summary>Encodes a construct header as a SQLite blob.</summary>
    /// <param name="h">Record.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodeConstructHeaderBlob(ConstructHeaderRecord h)
    {
        var w = new ByteWriter();
        w.WriteByte((byte)BlobKind.ConstructHeader);
        w.WriteByte(CurrentFormatVersion);
        WriteConstructHeader(w, h);
        return w.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a construct header blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The record.</returns>
    public static ConstructHeaderRecord DecodeConstructHeaderBlob(ReadOnlySpan<byte> blob)
    {
        ValidateBlobHeader(blob, BlobKind.ConstructHeader);
        var r = new ByteReader(blob.Slice(2));
        var result = ReadConstructHeader(ref r);
        r.EnsureEnd();
        return result;
    }

    /// <summary>Encodes a player record as a SQLite blob.</summary>
    /// <param name="p">Record.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodePlayerBlob(PlayerRecord p)
    {
        var w = new ByteWriter();
        w.WriteByte((byte)BlobKind.Player);
        w.WriteByte(CurrentFormatVersion);
        WritePlayer(w, p);
        return w.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a player blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The record.</returns>
    public static PlayerRecord DecodePlayerBlob(ReadOnlySpan<byte> blob)
    {
        ValidateBlobHeader(blob, BlobKind.Player);
        var r = new ByteReader(blob.Slice(2));
        var result = ReadPlayer(ref r);
        r.EnsureEnd();
        return result;
    }

    /// <summary>Encodes chunk blocks as a SQLite blob; the chunk key lives in table columns.</summary>
    /// <param name="blocks">Exactly 4096 packed block states.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodeChunkBlob(ReadOnlySpan<uint> blocks)
    {
        var w = new ByteWriter();
        w.WriteByte((byte)BlobKind.ConstructChunk);
        w.WriteByte(CurrentFormatVersion);
        WriteChunkBlocks(w, blocks);
        return w.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a chunk blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>A new array of 4096 packed block states.</returns>
    public static uint[] DecodeChunkBlob(ReadOnlySpan<byte> blob)
    {
        ValidateBlobHeader(blob, BlobKind.ConstructChunk);
        var r = new ByteReader(blob.Slice(2));
        var result = ReadChunkBlocks(ref r);
        r.EnsureEnd();
        return result;
    }

    /// <summary>Encodes planet parameters as a SQLite blob.</summary>
    /// <param name="p">Record.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodePlanetBlob(PlanetParams p)
    {
        var w = new ByteWriter();
        w.WriteByte((byte)BlobKind.PlanetParams);
        w.WriteByte(CurrentFormatVersion);
        WritePlanet(w, p);
        return w.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a planet blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The record.</returns>
    public static PlanetParams DecodePlanetBlob(ReadOnlySpan<byte> blob)
    {
        ValidateBlobHeader(blob, BlobKind.PlanetParams);
        var r = new ByteReader(blob.Slice(2));
        var result = ReadPlanet(ref r);
        r.EnsureEnd();
        return result;
    }

    /// <summary>
    /// Writes a star system without a prefix (K5-21 decision 13; also the payload of <c>StarSystemState</c>):
    /// [system u64][layout u8][home u64][sun cycle f32][count u8]{[planet u64][body index u8][position 3×f64][rotation 4×f64]}.
    /// </summary>
    /// <param name="w">Target writer.</param>
    /// <param name="system">The star system.</param>
    /// <exception cref="ArgumentException">The system breaks an invariant of <see cref="StarSystemParams"/>.</exception>
    public static void WriteStarSystem(ByteWriter w, StarSystemParams system)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(system);
        try
        {
            system.Validate();
        }
        catch (InvalidDataException ex)
        {
            throw new ArgumentException(ex.Message, nameof(system), ex);
        }

        w.WriteUInt64(system.System.Value);
        w.WriteByte(system.LayoutVersion);
        w.WriteUInt64(system.HomePlanet.Value);
        w.WriteSingle(system.SunCycleSeconds);
        w.WriteByte((byte)system.Bodies.Count);
        foreach (var body in system.Bodies)
        {
            w.WriteUInt64(body.Planet.Value);
            w.WriteByte(body.BodyIndex);
            WriteDouble(w, body.Pose.Position.X);
            WriteDouble(w, body.Pose.Position.Y);
            WriteDouble(w, body.Pose.Position.Z);
            WriteDouble(w, body.Pose.Rotation.X);
            WriteDouble(w, body.Pose.Rotation.Y);
            WriteDouble(w, body.Pose.Rotation.Z);
            WriteDouble(w, body.Pose.Rotation.W);
        }
    }

    /// <summary>Reads a star system written by <see cref="WriteStarSystem"/>.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>The star system.</returns>
    /// <exception cref="ProtocolViolationException">
    /// Truncated data, NaN or infinities, 0 or more than 8 bodies, duplicates, a missing home planet or body 0 off the origin.
    /// </exception>
    public static StarSystemParams ReadStarSystem(ref ByteReader r)
    {
        var system = new SystemId(r.ReadUInt64());
        var layout = r.ReadByte();
        var home = new PlanetId(r.ReadUInt64());
        var sun = r.ReadSingle();
        var count = r.ReadByte();
        if (count is 0 or > StarSystemRules.MaxBodies)
        {
            throw new ProtocolViolationException($"Star system with {count} bodies.");
        }

        var bodies = new BodyPlacement[count];
        for (var i = 0; i < count; i++)
        {
            var planet = new PlanetId(r.ReadUInt64());
            var index = r.ReadByte();
            var position = new Vector3D(ReadDouble(ref r), ReadDouble(ref r), ReadDouble(ref r));
            var rotation = new QuaternionD(ReadDouble(ref r), ReadDouble(ref r), ReadDouble(ref r), ReadDouble(ref r));
            bodies[i] = new BodyPlacement(planet, index, new PoseD(position, rotation));
        }

        var result = new StarSystemParams(system, layout, home, sun, bodies);
        try
        {
            result.Validate();
        }
        catch (InvalidDataException ex)
        {
            throw new ProtocolViolationException(ex.Message);
        }

        return result;
    }

    /// <summary>Encodes a star system as a SQLite blob (<see cref="BlobKind.StarSystemParams"/>).</summary>
    /// <param name="system">The star system.</param>
    /// <returns>The blob.</returns>
    /// <exception cref="ArgumentException">The system breaks an invariant.</exception>
    public static byte[] EncodeStarSystemBlob(StarSystemParams system)
    {
        var w = new ByteWriter();
        w.WriteByte((byte)BlobKind.StarSystemParams);
        w.WriteByte(CurrentFormatVersion);
        WriteStarSystem(w, system);
        return w.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a star system blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The star system.</returns>
    /// <exception cref="ProtocolViolationException">The blob is damaged (see <see cref="ReadStarSystem"/>).</exception>
    public static StarSystemParams DecodeStarSystemBlob(ReadOnlySpan<byte> blob)
    {
        ValidateBlobHeader(blob, BlobKind.StarSystemParams);
        var r = new ByteReader(blob.Slice(2));
        var result = ReadStarSystem(ref r);
        r.EnsureEnd();
        return result;
    }

    /// <summary>
    /// Writes a map marker without a prefix (K5-27; the body of operation 22, blob 11 and each record of <c>MarkersState</c>):
    /// [id varuint][hasOwner][owner varuint when 1][planet varuint][x, y, z f32][name][icon][colour].
    /// </summary>
    /// <param name="w">Target writer.</param>
    /// <param name="marker">The marker.</param>
    /// <exception cref="ArgumentException">A value is out of range.</exception>
    public static void WriteMarker(ByteWriter w, MarkerRecord marker)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(marker);
        if (!MarkerRules.IsValidStyle(marker.Icon, marker.Color))
        {
            throw new ArgumentException("Invalid marker icon or colour.", nameof(marker));
        }

        w.WriteVarUInt(marker.Id.Value);
        w.WriteBool(marker.Owner is not null);
        if (marker.Owner is { } owner)
        {
            w.WriteVarUInt(owner.Value);
        }

        w.WriteVarUInt(marker.Planet.Value);
        w.WriteVector3(marker.Position);
        w.WriteString(marker.Name, MarkerRules.MaxNameBytes);
        w.WriteByte((byte)marker.Icon);
        w.WriteByte((byte)marker.Color);
    }

    /// <summary>Reads a map marker.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>The marker.</returns>
    /// <exception cref="ProtocolViolationException">The data is damaged.</exception>
    public static MarkerRecord ReadMarker(ref ByteReader r)
    {
        var id = new MarkerId(r.ReadVarUInt());
        PlayerId? owner = r.ReadBool() ? new PlayerId(r.ReadVarUInt()) : null;
        var planet = new PlanetId(r.ReadVarUInt());
        var position = r.ReadVector3();
        string name = r.ReadString(MarkerRules.MaxNameBytes);
        byte icon = r.ReadByte();
        byte color = r.ReadByte();
        if (id.IsNone || id.Value > MarkerIdAllocator.MaxValue || owner is { IsNone: true } || icon > (byte)MarkerIcon.Poi
            || color > (byte)MarkerColor.Orange)
        {
            throw new ProtocolViolationException("Invalid marker.");
        }

        return new MarkerRecord(id, owner, planet, position, name, (MarkerIcon)icon, (MarkerColor)color);
    }

    /// <summary>Encodes a marker as a SQLite blob (<see cref="BlobKind.Marker"/>).</summary>
    /// <param name="marker">The marker.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodeMarkerBlob(MarkerRecord marker)
    {
        var w = new ByteWriter();
        w.WriteByte((byte)BlobKind.Marker);
        w.WriteByte(CurrentFormatVersion);
        WriteMarker(w, marker);
        return w.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a marker blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The marker.</returns>
    /// <exception cref="ProtocolViolationException">The blob is damaged.</exception>
    public static MarkerRecord DecodeMarkerBlob(ReadOnlySpan<byte> blob)
    {
        ValidateBlobHeader(blob, BlobKind.Marker);
        var r = new ByteReader(blob.Slice(2));
        var result = ReadMarker(ref r);
        r.EnsureEnd();
        return result;
    }

    /// <summary>
    /// Encodes a terrain block envelope: [kind][format version][voxel format][u32 length][voxels]. The whole blob is also the
    /// payload of <c>TerrainBlockData</c>, so it never exceeds <see cref="ProtocolConstants.MaxTerrainBlockDataBytes"/>.
    /// </summary>
    /// <param name="voxelFormat">Voxel payload format; 0 is reserved.</param>
    /// <param name="voxels">The serialized voxels.</param>
    /// <returns>The blob.</returns>
    /// <exception cref="ArgumentException">The voxel format is 0 or the blob would exceed the size limit.</exception>
    public static byte[] EncodeTerrainBlockBlob(byte voxelFormat, ReadOnlySpan<byte> voxels)
    {
        if (voxelFormat == 0)
        {
            throw new ArgumentException("Voxel format 0 is reserved.", nameof(voxelFormat));
        }

        if (voxels.Length > ProtocolConstants.MaxTerrainBlockDataBytes - TerrainBlockHeaderBytes)
        {
            throw new ArgumentException(
                $"Terrain block blob of {voxels.Length + TerrainBlockHeaderBytes} bytes exceeds {ProtocolConstants.MaxTerrainBlockDataBytes}.",
                nameof(voxels));
        }

        var blob = new byte[TerrainBlockHeaderBytes + voxels.Length];
        blob[0] = (byte)BlobKind.TerrainBlock;
        blob[1] = CurrentFormatVersion;
        blob[2] = voxelFormat;
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(3), (uint)voxels.Length);
        voxels.CopyTo(blob.AsSpan(TerrainBlockHeaderBytes));
        return blob;
    }

    /// <summary>Decodes a terrain block envelope; the voxels are copied.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="ProtocolViolationException">
    /// Wrong kind or format version, voxel format 0, a length that does not match the blob, or a blob above the size limit.
    /// </exception>
    public static TerrainBlockBlob DecodeTerrainBlockBlob(ReadOnlySpan<byte> blob)
    {
        if (blob.Length > ProtocolConstants.MaxTerrainBlockDataBytes)
        {
            throw new ProtocolViolationException(
                $"Terrain block blob of {blob.Length} bytes exceeds {ProtocolConstants.MaxTerrainBlockDataBytes}.");
        }

        ValidateBlobHeader(blob, BlobKind.TerrainBlock);
        if (blob.Length < TerrainBlockHeaderBytes)
        {
            throw new ProtocolViolationException($"Terrain block blob too short ({blob.Length} bytes).");
        }

        var voxelFormat = blob[2];
        if (voxelFormat == 0)
        {
            throw new ProtocolViolationException("Terrain block voxel format 0 is reserved.");
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(blob[3..]);
        if (length != (uint)(blob.Length - TerrainBlockHeaderBytes))
        {
            throw new ProtocolViolationException(
                $"Terrain block length {length} does not match the {blob.Length - TerrainBlockHeaderBytes} payload bytes.");
        }

        return new TerrainBlockBlob(voxelFormat, blob[TerrainBlockHeaderBytes..].ToArray());
    }

    private static void WriteDouble(ByteWriter w, double value) => w.WriteUInt64(BitConverter.DoubleToUInt64Bits(value));

    private static double ReadDouble(ref ByteReader r)
    {
        var value = BitConverter.UInt64BitsToDouble(r.ReadUInt64());
        if (!double.IsFinite(value))
        {
            throw new ProtocolViolationException("Double value must be finite.");
        }

        return value;
    }

    private static void ValidateBlobHeader(ReadOnlySpan<byte> blob, BlobKind expectedKind)
    {
        if (blob.Length < 2)
        {
            throw new ProtocolViolationException($"Blob too short ({blob.Length} bytes).");
        }

        if (blob[0] != (byte)expectedKind)
        {
            throw new ProtocolViolationException($"Blob kind {blob[0]} does not match expected {(byte)expectedKind}.");
        }

        if (blob[1] != CurrentFormatVersion)
        {
            throw new ProtocolViolationException($"Unsupported blob format version {blob[1]}.");
        }
    }
}
