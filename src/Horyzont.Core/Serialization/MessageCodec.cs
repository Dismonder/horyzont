using System.Buffers.Binary;
using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Markers;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Serialization;

/// <summary>Binary codec for protocol messages: message type, format version, payload.</summary>
public static class MessageCodec
{
    private const byte CurrentFormatVersion = 1;

    /// <summary>Appends the frame of <paramref name="message"/>; throws <see cref="ArgumentException"/> when it exceeds the channel limit.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="message">Message.</param>
    public static void Write(ByteWriter w, Message message)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(message);

        int startLength = w.Length;

        w.WriteByte((byte)message.Type);
        w.WriteByte(CurrentFormatVersion);

        WritePayload(w, message);

        int frameLength = w.Length - startLength;
        int limit = MessageChannels.MaxFrameBytes(MessageChannels.For(message.Type));
        if (frameLength > limit)
        {
            throw new ArgumentException($"Message {message.Type} frame length {frameLength} exceeds channel limit {limit}.", nameof(message));
        }
    }

    /// <summary>Decodes a whole frame; the channel limit is checked first and trailing bytes are an error.</summary>
    /// <param name="frame">Frame bytes.</param>
    /// <returns>The message.</returns>
    public static Message Read(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
        {
            throw new ProtocolViolationException($"Frame length {frame.Length} is shorter than minimum header (2 bytes).");
        }

        byte typeByte = frame[0];
        if (!Enum.IsDefined((MessageType)typeByte))
        {
            throw new ProtocolViolationException($"Unknown message type {typeByte}.");
        }

        var type = (MessageType)typeByte;
        int limit = MessageChannels.MaxFrameBytes(MessageChannels.For(type));
        if (frame.Length > limit)
        {
            throw new ProtocolViolationException($"Frame length {frame.Length} exceeds channel limit {limit} for {type}.");
        }

        byte version = frame[1];
        if (version != CurrentFormatVersion)
        {
            throw new ProtocolViolationException($"Unsupported format version {version} for {type}.");
        }

        var r = new ByteReader(frame.Slice(2));
        Message message = ReadPayload(ref r, type);
        r.EnsureEnd();
        return message;
    }

    /// <summary>Reads bytes 2-3 of a ClientHello frame whatever its FormatVersion.</summary>
    /// <param name="frame">Frame bytes.</param>
    /// <param name="version">The protocol version when the method returns true.</param>
    /// <returns>True when the frame is a ClientHello with a readable version.</returns>
    public static bool TryReadProtocolVersion(ReadOnlySpan<byte> frame, out ushort version)
    {
        if (frame.Length >= 4 && frame[0] == (byte)MessageType.ClientHello)
        {
            version = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(2, 2));
            return true;
        }

        version = 0;
        return false;
    }

    private static void WritePayload(ByteWriter w, Message message)
    {
        switch (message)
        {
            case ClientHello ch:
                w.WriteUInt16(ch.ProtocolVersion);
                w.WriteUInt64(ch.BuildHash);
                CoreCodecPrimitives.WritePlatformIdentity(w, ch.Identity);
                w.WriteString(ch.DisplayName, ProtocolConstants.MaxDisplayNameBytes);
                w.WriteString(ch.Password, ProtocolConstants.MaxPasswordBytes);
                w.WriteBytes(ch.AuthTicket, ProtocolConstants.MaxAuthTicketBytes);
                break;

            case ServerHello sh:
                w.WriteGuid(sh.World.Value);
                w.WriteVarUInt(sh.ServerTick.Value);
                w.WriteByte(sh.TickRate);
                w.WriteVarUInt(sh.YourPlayer.Value);
                w.WriteVarUInt(sh.YourEntity.Value);

                if (sh.Planets.Count > ProtocolConstants.MaxPlanetsInHello)
                {
                    throw new ArgumentException($"Planets count {sh.Planets.Count} exceeds limit {ProtocolConstants.MaxPlanetsInHello}.", nameof(message));
                }

                w.WriteVarUInt((ulong)sh.Planets.Count);
                for (int i = 0; i < sh.Planets.Count; i++)
                {
                    StateCodec.WritePlanet(w, sh.Planets[i]);
                }

                if (sh.BlockTypes.Count > ProtocolConstants.MaxBlockTypes)
                {
                    throw new ArgumentException($"BlockTypes count {sh.BlockTypes.Count} exceeds limit {ProtocolConstants.MaxBlockTypes}.", nameof(message));
                }

                w.WriteVarUInt((ulong)sh.BlockTypes.Count);
                for (int i = 0; i < sh.BlockTypes.Count; i++)
                {
                    StateCodec.WriteRegistryEntry(w, sh.BlockTypes[i], ProtocolConstants.MaxBlockTypes);
                }

                if (sh.Materials.Count > ProtocolConstants.MaxMaterials)
                {
                    throw new ArgumentException($"Materials count {sh.Materials.Count} exceeds limit {ProtocolConstants.MaxMaterials}.", nameof(message));
                }

                w.WriteVarUInt((ulong)sh.Materials.Count);
                for (int i = 0; i < sh.Materials.Count; i++)
                {
                    StateCodec.WriteRegistryEntry(w, sh.Materials[i], ProtocolConstants.MaxMaterials);
                }

                break;

            case HandshakeReject hr:
                w.WriteByte((byte)hr.Code);
                w.WriteString(hr.Detail, ProtocolConstants.MaxRejectDetailBytes);
                break;

            case ClientReady:
                // empty payload
                break;

            case TerrainSync sync:
                ValidateTerrainSync(sync);
                w.WriteVarUInt(sync.TransferId);
                w.WriteByte(sync.Purpose);
                w.WriteVarUInt(sync.Planet.Value);
                w.WriteVarUInt(sync.BarrierOpId.Value);
                w.WriteVarUInt(sync.PageIndex);
                w.WriteBool(sync.IsLastPage);
                w.WriteVarUInt((ulong)sync.Entries.Count);
                for (var i = 0; i < sync.Entries.Count; i++)
                {
                    var entry = sync.Entries[i];
                    WriteTerrainBlockKey(w, entry.Key);
                    w.WriteVarUInt(entry.Version);
                    w.WriteUInt64(entry.Hash);
                }
                break;

            case TerrainView view:
                if (!ValidTerrainView(view))
                {
                    throw new ArgumentException("Invalid terrain viewers.", nameof(message));
                }
                w.WriteVarUInt(view.Planet.Value);
                w.WriteVarUInt(view.ViewSeq);
                w.WriteVector3(view.VisualPosition);
                w.WriteSingle(view.VisualDistance);
                w.WriteVector3(view.CollisionPosition);
                w.WriteSingle(view.CollisionDistance);
                break;

            case TerrainCacheReport tcr:
                w.WriteVarUInt(tcr.Planet.Value);
                if (tcr.Entries.Count > ProtocolConstants.MaxTerrainCacheEntries)
                {
                    throw new ArgumentException($"TerrainCacheReport entries count {tcr.Entries.Count} exceeds limit {ProtocolConstants.MaxTerrainCacheEntries}.", nameof(message));
                }

                w.WriteVarUInt((ulong)tcr.Entries.Count);
                for (int i = 0; i < tcr.Entries.Count; i++)
                {
                    WriteTerrainBlockVersion(w, tcr.Entries[i]);
                }

                break;

            case TerrainBlockData tbd:
                WriteTerrainBlockKey(w, tbd.Key);
                w.WriteVarUInt(tbd.Version);
                w.WriteBytes(tbd.Data, ProtocolConstants.MaxTerrainBlockDataBytes);
                break;

            case TerrainChecksums tc:
                w.WriteVarUInt(tc.Planet.Value);
                if (tc.Entries.Count > ProtocolConstants.MaxTerrainChecksumEntries)
                {
                    throw new ArgumentException($"TerrainChecksums entries count {tc.Entries.Count} exceeds limit {ProtocolConstants.MaxTerrainChecksumEntries}.", nameof(message));
                }

                w.WriteVarUInt((ulong)tc.Entries.Count);
                for (int i = 0; i < tc.Entries.Count; i++)
                {
                    WriteTerrainBlockKey(w, tc.Entries[i].Key);
                    w.WriteUInt64(tc.Entries[i].Hash);
                }

                break;

            case ConstructState cs:
                StateCodec.WriteConstructHeader(w, cs.Header);

                if (cs.PartCount < 1 || cs.PartIndex >= cs.PartCount)
                {
                    throw new ArgumentException($"Invalid PartIndex {cs.PartIndex} for PartCount {cs.PartCount}.", nameof(message));
                }

                w.WriteVarUInt(cs.PartIndex);
                w.WriteVarUInt(cs.PartCount);

                if (cs.Chunks.Count > ProtocolConstants.MaxChunksPerConstructStatePart)
                {
                    throw new ArgumentException($"Chunks count {cs.Chunks.Count} exceeds limit {ProtocolConstants.MaxChunksPerConstructStatePart}.", nameof(message));
                }

                for (int i = 0; i < cs.Chunks.Count; i++)
                {
                    if (cs.Chunks[i].Key.Construct != cs.Header.Id)
                    {
                        throw new ArgumentException($"Chunk construct id {cs.Chunks[i].Key.Construct} does not match header {cs.Header.Id}.", nameof(message));
                    }

                    for (int j = i + 1; j < cs.Chunks.Count; j++)
                    {
                        if (cs.Chunks[i].Key.Chunk == cs.Chunks[j].Key.Chunk)
                        {
                            throw new ArgumentException("Duplicate chunk position in ConstructState.", nameof(message));
                        }
                    }
                }

                w.WriteVarUInt((ulong)cs.Chunks.Count);
                for (int i = 0; i < cs.Chunks.Count; i++)
                {
                    CoreCodecPrimitives.WriteChunkPos(w, cs.Chunks[i].Key.Chunk);
                    StateCodec.WriteChunkBlocks(w, cs.Chunks[i].Blocks);
                }

                break;

            case ConstructRemoved cr:
                w.WriteVarUInt(cr.Construct.Value);
                break;

            case OperationApplied oa:
                w.WriteVarUInt(oa.Id.Value);
                w.WriteVarUInt(oa.Tick.Value);
                OperationCodec.Write(w, oa.Operation);

                if (oa.ConstructChanges.Count > ProtocolConstants.MaxConstructChangesPerOperation)
                {
                    throw new ArgumentException($"ConstructChanges count {oa.ConstructChanges.Count} exceeds limit {ProtocolConstants.MaxConstructChangesPerOperation}.", nameof(message));
                }

                ulong prevConstruct = 0;
                for (int i = 0; i < oa.ConstructChanges.Count; i++)
                {
                    ulong current = oa.ConstructChanges[i].Construct.Value;
                    if (i > 0 && current <= prevConstruct)
                    {
                        throw new ArgumentException("ConstructChanges must be strictly ascending by Construct id.", nameof(message));
                    }

                    prevConstruct = current;
                }

                w.WriteVarUInt((ulong)oa.ConstructChanges.Count);
                for (int i = 0; i < oa.ConstructChanges.Count; i++)
                {
                    w.WriteVarUInt(oa.ConstructChanges[i].Construct.Value);
                    w.WriteVarUInt(oa.ConstructChanges[i].Version);
                    w.WriteBool(oa.ConstructChanges[i].Removed);
                }

                break;

            case IntentSubmit isMsg:
                w.WriteVarUInt(isMsg.ClientSeq);
                IntentCodec.Write(w, isMsg.Intent);
                break;

            case IntentReply ir:
                w.WriteVarUInt(ir.ClientSeq);
                if (!CoreCodecPrimitives.IsKnownRejectReason((byte)ir.Reason))
                {
                    throw new ArgumentException($"Invalid RejectReason {(byte)ir.Reason}.", nameof(message));
                }

                w.WriteByte((byte)ir.Reason);
                w.WriteVarUInt(ir.AppliedOpId.Value);
                break;

            case InputBatch ib:
                w.WriteVarUInt(ib.FirstSeq);
                if (ib.Inputs.Count < 1 || ib.Inputs.Count > ProtocolConstants.MaxInputsPerBatch)
                {
                    throw new ArgumentException($"InputBatch inputs count {ib.Inputs.Count} must be 1..{ProtocolConstants.MaxInputsPerBatch}.", nameof(message));
                }

                w.WriteByte((byte)ib.Inputs.Count);
                for (int i = 0; i < ib.Inputs.Count; i++)
                {
                    var input = ib.Inputs[i];
                    w.WriteVarUInt(input.ClientTick);
                    w.WriteSByte(Quantization.QuantizeAxis(input.MoveX));
                    w.WriteSByte(Quantization.QuantizeAxis(input.MoveY));
                    w.WriteSingle(input.Yaw);
                    w.WriteSingle(input.Pitch);

                    if (((ushort)input.Buttons & ~ProtocolConstants.InputButtonsMask) != 0)
                    {
                        throw new ArgumentException($"InputButtons contains bits outside the 0x{ProtocolConstants.InputButtonsMask:X} mask ({(ushort)input.Buttons}).", nameof(message));
                    }

                    w.WriteUInt16((ushort)input.Buttons);
                }

                break;

            case Snapshot s:
                w.WriteVarUInt(s.SnapshotId);
                w.WriteVarUInt(s.ServerTick.Value);
                w.WriteVarUInt(s.BaselineSnapshotId);
                w.WriteVarUInt(s.LastProcessedInputSeq);

                if (s.Frames.Count < 1 || s.Frames.Count > ProtocolConstants.MaxFramesPerSnapshot)
                {
                    throw new ArgumentException($"Frames count {s.Frames.Count} must be 1..{ProtocolConstants.MaxFramesPerSnapshot}.", nameof(message));
                }

                for (int i = 0; i < s.Frames.Count; i++)
                {
                    for (int j = i + 1; j < s.Frames.Count; j++)
                    {
                        if (s.Frames[i].Frame == s.Frames[j].Frame)
                        {
                            throw new ArgumentException("Duplicate frame in snapshot frames table.", nameof(message));
                        }
                    }
                }

                w.WriteVarUInt((ulong)s.Frames.Count);
                for (int i = 0; i < s.Frames.Count; i++)
                {
                    CoreCodecPrimitives.WriteFrameRef(w, s.Frames[i].Frame);
                    w.WriteVarUInt(s.Frames[i].ParentSeq);
                }

                if (s.Entities.Count > ProtocolConstants.MaxEntitiesPerSnapshot)
                {
                    throw new ArgumentException($"Entities count {s.Entities.Count} exceeds limit {ProtocolConstants.MaxEntitiesPerSnapshot}.", nameof(message));
                }

                w.WriteVarUInt((ulong)s.Entities.Count);
                for (int i = 0; i < s.Entities.Count; i++)
                {
                    WriteEntityState(w, s.Entities[i], s.Frames.Count);
                }

                break;

            case StarSystemState starSystem:
                StateCodec.WriteStarSystem(w, starSystem.System);
                break;

            case ShipStatus shipStatus:
                WriteShipStatus(w, shipStatus);
                break;

            case MarkersState markers:
                if (markers.Markers.Count > ProtocolConstants.MaxMarkersInState)
                {
                    throw new ArgumentException("Too many markers.", nameof(message));
                }

                w.WriteVarUInt((ulong)markers.Markers.Count);
                for (int i = 0; i < markers.Markers.Count; i++)
                {
                    StateCodec.WriteMarker(w, markers.Markers[i]);
                }

                break;

            case ResyncRequest request:
                if (request.Entity.IsNone)
                {
                    throw new ArgumentException("Resync entity must be nonzero.", nameof(message));
                }

                w.WriteVarUInt(request.Entity.Value);
                break;

            case SnapshotAck sa:
                w.WriteVarUInt(sa.SnapshotId);
                break;

            case EntitySpawn es:
                w.WriteVarUInt(es.Entity.Value);
                if (es.Kind < EntityKind.Player || es.Kind > EntityKind.Construct)
                {
                    throw new ArgumentException($"Invalid EntityKind {(byte)es.Kind}.", nameof(message));
                }

                w.WriteByte((byte)es.Kind);
                w.WriteVarUInt(es.Archetype);

                if (es.Owner.HasValue)
                {
                    w.WriteBool(true);
                    w.WriteVarUInt(es.Owner.Value.Value);
                }
                else
                {
                    w.WriteBool(false);
                }

                CoreCodecPrimitives.WriteFrameRef(w, es.Frame.Frame);
                w.WriteVarUInt(es.Frame.ParentSeq);

                if (es.Initial.FrameIndex != 0)
                {
                    throw new ArgumentException($"Initial EntityState FrameIndex must be 0 (got {es.Initial.FrameIndex}).", nameof(message));
                }

                WriteEntityState(w, es.Initial, 1);
                w.WriteBytes(es.KindData, ProtocolConstants.MaxEntityKindDataBytes);
                break;

            case EntityDespawn ed:
                w.WriteVarUInt(ed.Entity.Value);
                break;

            case Ping ping:
                w.WriteUInt64(ping.ClientTimeMicros);
                break;

            case Pong pong:
                w.WriteUInt64(pong.ClientTimeMicros);
                w.WriteVarUInt(pong.ServerTick.Value);
                break;

            case ServerStatus status:
                w.WriteBool(status.ReadOnly);
                w.WriteByte(status.Reason);
                w.WriteString(status.Detail, ProtocolConstants.MaxRejectDetailBytes);
                break;

            case JoinUnavailable unavailable:
                w.WriteByte(unavailable.Reason);
                w.WriteString(unavailable.Detail, ProtocolConstants.MaxRejectDetailBytes);
                break;

            case Disconnect dc:
                if (dc.Code < DisconnectCode.Normal || dc.Code > DisconnectCode.HandshakeTimeout)
                {
                    throw new ArgumentException($"Invalid DisconnectCode {(byte)dc.Code}.", nameof(message));
                }

                w.WriteByte((byte)dc.Code);
                break;

            case ContainerState cst:
                WriteContainerState(w, cst);
                break;

            case ContainerDelta cd:
                w.WriteVarUInt(cd.Container.Value);
                w.WriteVarUInt(cd.Version);
                WriteSlotContents(w, cd.Changed, ProtocolConstants.MaxContainerSlots, nameof(message));
                break;

            case ContainerSubscribe sub:
                w.WriteVarUInt(sub.Container.Value);
                break;

            case ContainerUnsubscribe unsub:
                w.WriteVarUInt(unsub.Container.Value);
                break;

            case BlockEntityStates bes:
                WriteBlockEntityStates(w, bes);
                break;

            case PropStates ps:
                WritePropStates(w, ps);
                break;

            case PlayerProgressState progress:
                WritePlayerProgressState(w, progress);
                break;

            case ItemRegistry ireg:
                if (ireg.Entries.Count > ProtocolConstants.MaxItemTypes)
                {
                    throw new ArgumentException($"ItemRegistry entries count {ireg.Entries.Count} exceeds limit {ProtocolConstants.MaxItemTypes}.", nameof(message));
                }

                w.WriteVarUInt((ulong)ireg.Entries.Count);
                for (int i = 0; i < ireg.Entries.Count; i++)
                {
                    StateCodec.WriteRegistryEntry(w, ireg.Entries[i], ProtocolConstants.MaxItemTypes);
                }

                break;

            default:
                throw new ArgumentException($"Unknown message type {message.GetType().Name}.", nameof(message));
        }
    }

    private static Message ReadPayload(ref ByteReader r, MessageType type)
    {
        switch (type)
        {
            case MessageType.ClientHello:
                ushort protoVersion = r.ReadUInt16();
                ulong buildHash = r.ReadUInt64();
                var identity = CoreCodecPrimitives.ReadPlatformIdentity(ref r);
                string displayName = r.ReadString(ProtocolConstants.MaxDisplayNameBytes);
                string password = r.ReadString(ProtocolConstants.MaxPasswordBytes);
                byte[] ticket = r.ReadBytes(ProtocolConstants.MaxAuthTicketBytes);
                return new ClientHello(protoVersion, buildHash, identity, displayName, password, ticket);

            case MessageType.ServerHello:
                var world = new WorldId(r.ReadGuid());
                var serverTick = new Tick(r.ReadVarUInt());
                byte tickRate = r.ReadByte();
                var yourPlayer = new PlayerId(r.ReadVarUInt());
                var yourEntity = new EntityId(r.ReadVarUInt());

                int planetCount = r.ReadCount(ProtocolConstants.MaxPlanetsInHello);
                if (r.Remaining < planetCount * 12)
                {
                    throw new ProtocolViolationException("Not enough data for declared planets.");
                }

                var planets = new PlanetParams[planetCount];
                for (int i = 0; i < planetCount; i++)
                {
                    planets[i] = StateCodec.ReadPlanet(ref r);
                }

                int blockTypeCount = r.ReadCount(ProtocolConstants.MaxBlockTypes);
                if (r.Remaining < blockTypeCount * 2)
                {
                    throw new ProtocolViolationException("Not enough data for declared block types.");
                }

                var blockTypes = new RegistryEntry[blockTypeCount];
                for (int i = 0; i < blockTypeCount; i++)
                {
                    blockTypes[i] = StateCodec.ReadRegistryEntry(ref r, ProtocolConstants.MaxBlockTypes);
                }

                int materialCount = r.ReadCount(ProtocolConstants.MaxMaterials);
                if (r.Remaining < materialCount * 2)
                {
                    throw new ProtocolViolationException("Not enough data for declared materials.");
                }

                var materials = new RegistryEntry[materialCount];
                for (int i = 0; i < materialCount; i++)
                {
                    materials[i] = StateCodec.ReadRegistryEntry(ref r, ProtocolConstants.MaxMaterials);
                }

                return new ServerHello(world, serverTick, tickRate, yourPlayer, yourEntity, planets, blockTypes, materials);

            case MessageType.HandshakeReject:
                byte codeByte = r.ReadByte();
                string detail = r.ReadString(ProtocolConstants.MaxRejectDetailBytes);
                return new HandshakeReject((HandshakeRejectCode)codeByte, detail);

            case MessageType.ClientReady:
                return new ClientReady();

            case MessageType.TerrainSync:
                uint transferId = r.ReadVarUInt32();
                byte purpose = r.ReadByte();
                var planet = new PlanetId(r.ReadVarUInt());
                var barrier = new OpId(r.ReadVarUInt());
                ushort page = r.ReadVarUInt16();
                bool last = r.ReadBool();
                int count = r.ReadCount(ProtocolConstants.MaxTerrainSyncEntries);
                if (!ValidTerrainSyncHeader(transferId, purpose, planet, barrier, last, count))
                {
                    throw new ProtocolViolationException("Invalid terrain manifest header.");
                }
                // Each entry needs at least planet, lod, xyz, version and an eight-byte hash.
                if (count > r.Remaining / 14)
                {
                    throw new ProtocolViolationException("Truncated terrain manifest entries.");
                }
                var entries = new TerrainSyncEntry[count];
                for (var i = 0; i < count; i++)
                {
                    var key = ReadTerrainBlockKey(ref r);
                    if (key.Planet != planet || key.Lod != 0 || (i > 0 && entries[i - 1].Key >= key))
                    {
                        throw new ProtocolViolationException("Terrain manifest keys must be sorted distinct LOD zero keys of its planet.");
                    }
                    entries[i] = new TerrainSyncEntry(key, r.ReadVarUInt32(), r.ReadUInt64());
                }
                return new TerrainSync(transferId, purpose, planet, barrier, page, last, entries);

            case MessageType.TerrainView:
                var terrainView = new TerrainView(new PlanetId(r.ReadVarUInt()), r.ReadVarUInt32(),
                    r.ReadVector3(), r.ReadSingle(), r.ReadVector3(), r.ReadSingle());
                if (!ValidTerrainView(terrainView))
                {
                    throw new ProtocolViolationException("Invalid terrain viewers.");
                }
                return terrainView;

            case MessageType.TerrainCacheReport:
                var tcrPlanet = new PlanetId(r.ReadVarUInt());
                int tcrCount = r.ReadCount(ProtocolConstants.MaxTerrainCacheEntries);
                if (r.Remaining < tcrCount * 6)
                {
                    throw new ProtocolViolationException("Not enough data for declared cache report entries.");
                }

                var tcrEntries = new TerrainBlockVersion[tcrCount];
                for (int i = 0; i < tcrCount; i++)
                {
                    tcrEntries[i] = ReadTerrainBlockVersion(ref r);
                }

                return new TerrainCacheReport(tcrPlanet, tcrEntries);

            case MessageType.TerrainBlockData:
                var tbdKey = ReadTerrainBlockKey(ref r);
                uint tbdVersion = r.ReadVarUInt32();
                byte[] tbdData = r.ReadBytes(ProtocolConstants.MaxTerrainBlockDataBytes);
                return new TerrainBlockData(tbdKey, tbdVersion, tbdData);

            case MessageType.TerrainChecksums:
                var tcPlanet = new PlanetId(r.ReadVarUInt());
                int tcCount = r.ReadCount(ProtocolConstants.MaxTerrainChecksumEntries);
                if (r.Remaining < tcCount * 13)
                {
                    throw new ProtocolViolationException("Not enough data for declared checksum entries.");
                }

                var tcEntries = new TerrainBlockChecksum[tcCount];
                for (int i = 0; i < tcCount; i++)
                {
                    var key = ReadTerrainBlockKey(ref r);
                    ulong hash = r.ReadUInt64();
                    tcEntries[i] = new TerrainBlockChecksum(key, hash);
                }

                return new TerrainChecksums(tcPlanet, tcEntries);

            case MessageType.ConstructState:
                var header = StateCodec.ReadConstructHeader(ref r);
                ushort partIndex = r.ReadVarUInt16();
                ushort partCount = r.ReadVarUInt16();
                if (partCount < 1 || partIndex >= partCount)
                {
                    throw new ProtocolViolationException($"Invalid PartIndex {partIndex} for PartCount {partCount}.");
                }

                int chunkCount = r.ReadCount(ProtocolConstants.MaxChunksPerConstructStatePart);
                if (r.Remaining < chunkCount * 9)
                {
                    throw new ProtocolViolationException("Not enough data for declared chunks.");
                }

                var chunks = new ConstructChunkRecord[chunkCount];
                for (int i = 0; i < chunkCount; i++)
                {
                    var chunkPos = CoreCodecPrimitives.ReadChunkPos(ref r);
                    for (int j = 0; j < i; j++)
                    {
                        if (chunks[j].Key.Chunk == chunkPos)
                        {
                            throw new ProtocolViolationException("Duplicate chunk position in ConstructState.");
                        }
                    }

                    var blocks = StateCodec.ReadChunkBlocks(ref r);
                    chunks[i] = new ConstructChunkRecord(new ConstructChunkKey(header.Id, chunkPos), blocks);
                }

                return new ConstructState(header, partIndex, partCount, chunks);

            case MessageType.ConstructRemoved:
                var crConstruct = new EntityId(r.ReadVarUInt());
                return new ConstructRemoved(crConstruct);

            case MessageType.OperationApplied:
                var opId = new OpId(r.ReadVarUInt());
                var opTick = new Tick(r.ReadVarUInt());
                var op = OperationCodec.Read(ref r);

                int changeCount = r.ReadCount(ProtocolConstants.MaxConstructChangesPerOperation);
                if (r.Remaining < changeCount * 3)
                {
                    throw new ProtocolViolationException("Not enough data for declared construct changes.");
                }

                var changes = new ConstructChange[changeCount];
                ulong prevConstructId = 0;
                for (int i = 0; i < changeCount; i++)
                {
                    var construct = new EntityId(r.ReadVarUInt());
                    if (i > 0 && construct.Value <= prevConstructId)
                    {
                        throw new ProtocolViolationException("ConstructChanges must be strictly ascending by Construct id.");
                    }

                    prevConstructId = construct.Value;
                    uint v = r.ReadVarUInt32();
                    bool rem = r.ReadBool();
                    changes[i] = new ConstructChange(construct, v, rem);
                }

                return new OperationApplied(opId, opTick, op, changes);

            case MessageType.IntentSubmit:
                uint isSeq = r.ReadVarUInt32();
                var intent = IntentCodec.Read(ref r);
                return new IntentSubmit(isSeq, intent);

            case MessageType.IntentReply:
                uint irSeq = r.ReadVarUInt32();
                byte reasonByte = r.ReadByte();
                if (!CoreCodecPrimitives.IsKnownRejectReason(reasonByte))
                {
                    throw new ProtocolViolationException($"Unknown reject reason {reasonByte}.");
                }

                var reason = (RejectReason)reasonByte;
                var appliedOp = new OpId(r.ReadVarUInt());
                return new IntentReply(irSeq, reason, appliedOp);

            case MessageType.InputBatch:
                uint firstSeq = r.ReadVarUInt32();
                byte inputCount = r.ReadByte();
                if (inputCount < 1 || inputCount > ProtocolConstants.MaxInputsPerBatch)
                {
                    throw new ProtocolViolationException($"InputBatch input count {inputCount} must be 1..{ProtocolConstants.MaxInputsPerBatch}.");
                }

                if (r.Remaining < inputCount * 13)
                {
                    throw new ProtocolViolationException("Not enough data for declared inputs.");
                }

                var inputs = new PlayerInput[inputCount];
                for (int i = 0; i < inputCount; i++)
                {
                    uint cTick = r.ReadVarUInt32();
                    float moveX = Quantization.DequantizeAxis(r.ReadSByte());
                    float moveY = Quantization.DequantizeAxis(r.ReadSByte());
                    float yaw = r.ReadSingle();
                    float pitch = r.ReadSingle();
                    ushort buttons = r.ReadUInt16();
                    if ((buttons & ~ProtocolConstants.InputButtonsMask) != 0)
                    {
                        throw new ProtocolViolationException($"InputButtons contains bits outside the 0x{ProtocolConstants.InputButtonsMask:X} mask ({buttons}).");
                    }

                    inputs[i] = new PlayerInput(cTick, moveX, moveY, yaw, pitch, (InputButtons)buttons);
                }

                return new InputBatch(firstSeq, inputs);

            case MessageType.Snapshot:
                uint snapId = r.ReadVarUInt32();
                var sTick = new Tick(r.ReadVarUInt());
                uint baseline = r.ReadVarUInt32();
                uint lastInput = r.ReadVarUInt32();

                int frameCount = r.ReadCount(ProtocolConstants.MaxFramesPerSnapshot);
                if (frameCount < 1)
                {
                    throw new ProtocolViolationException("Snapshot must have at least 1 frame.");
                }

                var frames = new SnapshotFrame[frameCount];
                for (int i = 0; i < frameCount; i++)
                {
                    var fRef = CoreCodecPrimitives.ReadFrameRef(ref r);
                    for (int j = 0; j < i; j++)
                    {
                        if (frames[j].Frame == fRef)
                        {
                            throw new ProtocolViolationException("Duplicate frame in snapshot frames table.");
                        }
                    }

                    uint pSeq = r.ReadVarUInt32();
                    frames[i] = new SnapshotFrame(fRef, pSeq);
                }

                int entityCount = r.ReadCount(ProtocolConstants.MaxEntitiesPerSnapshot);
                if (r.Remaining < entityCount * 4)
                {
                    throw new ProtocolViolationException("Not enough data for declared entities.");
                }

                var entities = new EntityState[entityCount];
                for (int i = 0; i < entityCount; i++)
                {
                    entities[i] = ReadEntityState(ref r, frameCount);
                }

                return new Snapshot(snapId, sTick, baseline, lastInput, frames, entities);

            case MessageType.StarSystemState:
                return new StarSystemState(StateCodec.ReadStarSystem(ref r));

            case MessageType.ShipStatus:
                return ReadShipStatus(ref r);

            case MessageType.MarkersState:
                int markerCount = r.ReadCount(ProtocolConstants.MaxMarkersInState);
                var markerList = new MarkerRecord[markerCount];
                for (int i = 0; i < markerCount; i++)
                {
                    markerList[i] = StateCodec.ReadMarker(ref r);
                }

                return new MarkersState(markerList);

            case MessageType.ResyncRequest:
                var requested = new EntityId(r.ReadVarUInt());
                if (requested.IsNone)
                {
                    throw new ProtocolViolationException("Resync entity must be nonzero.");
                }

                return new ResyncRequest(requested);

            case MessageType.SnapshotAck:
                uint saSnapId = r.ReadVarUInt32();
                return new SnapshotAck(saSnapId);

            case MessageType.EntitySpawn:
                var spawnEntity = new EntityId(r.ReadVarUInt());
                byte kindByte = r.ReadByte();
                if (kindByte < (byte)EntityKind.Player || kindByte > (byte)EntityKind.Construct)
                {
                    throw new ProtocolViolationException($"Unknown entity kind {kindByte}.");
                }

                ushort archetype = r.ReadVarUInt16();

                bool hasOwner = r.ReadBool();
                PlayerId? owner = hasOwner ? new PlayerId(r.ReadVarUInt()) : null;

                var spawnFrameRef = CoreCodecPrimitives.ReadFrameRef(ref r);
                uint spawnParentSeq = r.ReadVarUInt32();
                var spawnFrame = new SnapshotFrame(spawnFrameRef, spawnParentSeq);

                var initial = ReadEntityState(ref r, 1);
                if (initial.FrameIndex != 0)
                {
                    throw new ProtocolViolationException("Initial entity state FrameIndex must be 0.");
                }

                byte[] kindData = r.ReadBytes(ProtocolConstants.MaxEntityKindDataBytes);
                return new EntitySpawn(spawnEntity, (EntityKind)kindByte, archetype, owner, spawnFrame, initial, kindData);

            case MessageType.EntityDespawn:
                var edEntity = new EntityId(r.ReadVarUInt());
                return new EntityDespawn(edEntity);

            case MessageType.Ping:
                ulong pingMicros = r.ReadUInt64();
                return new Ping(pingMicros);

            case MessageType.Pong:
                ulong pongMicros = r.ReadUInt64();
                var pongTick = new Tick(r.ReadVarUInt());
                return new Pong(pongMicros, pongTick);

            case MessageType.ServerStatus:
                return new ServerStatus(r.ReadBool(), r.ReadByte(), r.ReadString(ProtocolConstants.MaxRejectDetailBytes));

            case MessageType.JoinUnavailable:
                return new JoinUnavailable(r.ReadByte(), r.ReadString(ProtocolConstants.MaxRejectDetailBytes));

            case MessageType.Disconnect:
                byte dcByte = r.ReadByte();
                if (dcByte > (byte)DisconnectCode.HandshakeTimeout)
                {
                    throw new ProtocolViolationException($"Unknown disconnect code {dcByte}.");
                }

                return new Disconnect((DisconnectCode)dcByte);

            case MessageType.ContainerState:
                return ReadContainerState(ref r);

            case MessageType.ContainerDelta:
                var cdContainer = new ContainerId(r.ReadVarUInt());
                uint cdVersion = r.ReadVarUInt32();
                var changed = ReadSlotContents(ref r, ProtocolConstants.MaxContainerSlots);
                return new ContainerDelta(cdContainer, cdVersion, changed);

            case MessageType.ContainerSubscribe:
                return new ContainerSubscribe(new ContainerId(r.ReadVarUInt()));

            case MessageType.ContainerUnsubscribe:
                return new ContainerUnsubscribe(new ContainerId(r.ReadVarUInt()));

            case MessageType.BlockEntityStates:
                return ReadBlockEntityStates(ref r);

            case MessageType.PropStates:
                return ReadPropStates(ref r);

            case MessageType.PlayerProgressState:
                return ReadPlayerProgressState(ref r);

            case MessageType.ItemRegistry:
                ulong entryCount = r.ReadVarUInt();
                if (entryCount > ProtocolConstants.MaxItemTypes)
                {
                    throw new ProtocolViolationException($"ItemRegistry entries count {entryCount} exceeds limit {ProtocolConstants.MaxItemTypes}.");
                }

                var itemEntries = new RegistryEntry[(int)entryCount];
                for (int i = 0; i < itemEntries.Length; i++)
                {
                    itemEntries[i] = StateCodec.ReadRegistryEntry(ref r, ProtocolConstants.MaxItemTypes);
                }

                return new ItemRegistry(itemEntries);

            default:
                throw new ProtocolViolationException($"Unhandled message type {type}.");
        }
    }

    private static void WritePlayerProgressState(ByteWriter w, PlayerProgressState state)
    {
        ArgumentNullException.ThrowIfNull(state.Discovered);
        ArgumentNullException.ThrowIfNull(state.WorldProgressKeys);
        if (state.WorldProgressKeys.Count > ProtocolConstants.MaxProgressKeys)
        {
            throw new ArgumentException(
                $"PlayerProgressState keys count {state.WorldProgressKeys.Count} exceeds limit {ProtocolConstants.MaxProgressKeys}.", nameof(state));
        }

        w.WriteBytes(state.Discovered.Bytes, ProtocolConstants.MaxDiscoveredBytes);
        w.WriteVarUInt((ulong)state.WorldProgressKeys.Count);
        for (var i = 0; i < state.WorldProgressKeys.Count; i++)
        {
            var key = state.WorldProgressKeys[i];
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A progress key cannot be empty.", nameof(state));
            }

            if (i > 0 && string.CompareOrdinal(state.WorldProgressKeys[i - 1], key) >= 0)
            {
                throw new ArgumentException("PlayerProgressState keys must be strictly ascending in ordinal order.", nameof(state));
            }

            w.WriteString(key, ProtocolConstants.MaxRegistryKeyBytes);
        }
    }

    private static PlayerProgressState ReadPlayerProgressState(ref ByteReader r)
    {
        ItemTypeBitSet discovered;
        try
        {
            discovered = ItemTypeBitSet.FromBytes(r.ReadBytes(ProtocolConstants.MaxDiscoveredBytes));
        }
        catch (ArgumentException ex)
        {
            throw new ProtocolViolationException("The discovered item types are not canonical: " + ex.Message);
        }

        var count = r.ReadCount(ProtocolConstants.MaxProgressKeys);
        var keys = new string[count];
        for (var i = 0; i < count; i++)
        {
            var key = r.ReadString(ProtocolConstants.MaxRegistryKeyBytes);
            if (key.Length == 0)
            {
                throw new ProtocolViolationException("A progress key cannot be empty.");
            }

            if (i > 0 && string.CompareOrdinal(keys[i - 1], key) >= 0)
            {
                throw new ProtocolViolationException("PlayerProgressState keys must be strictly ascending in ordinal order.");
            }

            keys[i] = key;
        }

        return new PlayerProgressState(discovered, keys);
    }

    private static void WriteBlockEntityStates(ByteWriter w, BlockEntityStates states)
    {
        ArgumentNullException.ThrowIfNull(states.Entries);
        if (states.Entries.Count > ProtocolConstants.MaxBlockEntitiesPerConstruct)
        {
            throw new ArgumentException(
                $"BlockEntityStates entries count {states.Entries.Count} exceeds limit {ProtocolConstants.MaxBlockEntitiesPerConstruct}.", nameof(states));
        }

        w.WriteVarUInt(states.Construct.Value);
        w.WriteVarUInt((ulong)states.Entries.Count);
        BlockRef previous = default;
        for (int i = 0; i < states.Entries.Count; i++)
        {
            var entry = states.Entries[i];
            var key = new BlockRef(states.Construct, entry.Cell);
            if (i > 0 && key.CompareTo(previous) <= 0)
            {
                throw new ArgumentException("BlockEntityStates entries must be strictly ascending by cell (chunk, then index in the chunk).", nameof(states));
            }

            previous = key;
            CoreCodecPrimitives.WriteCellPos(w, entry.Cell);
            CoreCodecPrimitives.WriteBlockEntityState(w, entry.State, allowNone: false);
        }
    }

    private static BlockEntityStates ReadBlockEntityStates(ref ByteReader r)
    {
        var construct = new EntityId(r.ReadVarUInt());
        ulong count = r.ReadVarUInt();
        if (count > ProtocolConstants.MaxBlockEntitiesPerConstruct)
        {
            throw new ProtocolViolationException($"BlockEntityStates entries count {count} exceeds limit {ProtocolConstants.MaxBlockEntitiesPerConstruct}.");
        }

        var entries = new BlockEntityEntry[(int)count];
        BlockRef previous = default;
        for (int i = 0; i < entries.Length; i++)
        {
            var cell = CoreCodecPrimitives.ReadCellPos(ref r);
            var state = CoreCodecPrimitives.ReadBlockEntityState(ref r, allowNone: false)!;
            var key = new BlockRef(construct, cell);
            if (i > 0 && key.CompareTo(previous) <= 0)
            {
                throw new ProtocolViolationException("BlockEntityStates entries are not strictly ascending by cell.");
            }

            previous = key;
            entries[i] = new BlockEntityEntry(cell, state);
        }

        return new BlockEntityStates(construct, entries);
    }

    /// <summary>Most prop identifiers a region of 8 x 8 cells and 4 slots can hold (K6-31 D4).</summary>
    private const int MaxPropsPerRegion = 256;

    private static void WritePropStates(ByteWriter w, PropStates states)
    {
        ArgumentNullException.ThrowIfNull(states.Regions);
        if (states.Regions.Count is < 1 or > ProtocolConstants.MaxPropRegionsPerMessage)
        {
            throw new ArgumentException(
                $"PropStates regions count {states.Regions.Count} is outside 1..{ProtocolConstants.MaxPropRegionsPerMessage}.", nameof(states));
        }

        w.WriteVarUInt(states.Planet.Value);
        w.WriteVarUInt((ulong)states.Regions.Count);
        long total = 0;
        PropRegionKey previousRegion = default;
        for (int i = 0; i < states.Regions.Count; i++)
        {
            var region = states.Regions[i];
            ArgumentNullException.ThrowIfNull(region.Props);
            if (!region.Region.IsValid)
            {
                throw new ArgumentException($"Invalid PropStates region key {region.Region.Value}.", nameof(states));
            }

            if (i > 0 && region.Region.Value <= previousRegion.Value)
            {
                throw new ArgumentException("PropStates regions must be strictly ascending.", nameof(states));
            }

            if (region.Props.Count > MaxPropsPerRegion)
            {
                throw new ArgumentException($"PropStates region entries count {region.Props.Count} exceeds {MaxPropsPerRegion}.", nameof(states));
            }

            total += region.Props.Count;
            if (total > ProtocolConstants.MaxPropEntriesPerMessage)
            {
                throw new ArgumentException($"PropStates entries count exceeds limit {ProtocolConstants.MaxPropEntriesPerMessage}.", nameof(states));
            }

            previousRegion = region.Region;
            w.WriteVarUInt(region.Region.Value);
            w.WriteVarUInt((ulong)region.Props.Count);
            PropId previous = default;
            for (int j = 0; j < region.Props.Count; j++)
            {
                var entry = region.Props[j];
                if (entry.Prop.Region != region.Region)
                {
                    throw new ArgumentException("PropStates entries must belong to their region.", nameof(states));
                }

                if (j > 0 && entry.Prop.Value <= previous.Value)
                {
                    throw new ArgumentException("PropStates entries must be strictly ascending.", nameof(states));
                }

                previous = entry.Prop;
                CoreCodecPrimitives.WritePropId(w, entry.Prop);
                w.WriteVarUInt(entry.Tick.Value);
            }
        }
    }

    private static PropStates ReadPropStates(ref ByteReader r)
    {
        var planet = new PlanetId(r.ReadVarUInt());
        ulong regionCount = r.ReadVarUInt();
        if (regionCount is < 1 or > ProtocolConstants.MaxPropRegionsPerMessage)
        {
            throw new ProtocolViolationException($"PropStates regions count {regionCount} is outside 1..{ProtocolConstants.MaxPropRegionsPerMessage}.");
        }

        var regions = new PropRegionState[(int)regionCount];
        long total = 0;
        PropRegionKey previousRegion = default;
        for (int i = 0; i < regions.Length; i++)
        {
            var key = new PropRegionKey(r.ReadVarUInt());
            if (!key.IsValid)
            {
                throw new ProtocolViolationException($"Invalid PropStates region key {key.Value}.");
            }

            if (i > 0 && key.Value <= previousRegion.Value)
            {
                throw new ProtocolViolationException("PropStates regions are not strictly ascending.");
            }

            ulong count = r.ReadVarUInt();
            if (count > MaxPropsPerRegion)
            {
                throw new ProtocolViolationException($"PropStates region entries count {count} exceeds {MaxPropsPerRegion}.");
            }

            total += (long)count;
            if (total > ProtocolConstants.MaxPropEntriesPerMessage)
            {
                throw new ProtocolViolationException($"PropStates entries count exceeds limit {ProtocolConstants.MaxPropEntriesPerMessage}.");
            }

            previousRegion = key;
            var entries = new HarvestedPropEntry[(int)count];
            PropId previous = default;
            for (int j = 0; j < entries.Length; j++)
            {
                var prop = CoreCodecPrimitives.ReadPropId(ref r);
                if (prop.Region != key)
                {
                    throw new ProtocolViolationException("PropStates entry outside its region.");
                }

                if (j > 0 && prop.Value <= previous.Value)
                {
                    throw new ProtocolViolationException("PropStates entries are not strictly ascending.");
                }

                previous = prop;
                entries[j] = new HarvestedPropEntry(prop, new Tick(r.ReadVarUInt()));
            }

            regions[i] = new PropRegionState(key, entries);
        }

        return new PropStates(planet, regions);
    }

    /// <summary>Length of a whole <see cref="ShipStatus"/> frame: type, format version and 30 payload bytes.</summary>
    public const int ShipStatusFrameBytes = 32;

    /// <summary>
    /// Encodes a whole <see cref="ShipStatus"/> frame without allocating (K5-26 decision 9), the same bytes as <see cref="Write"/>.
    /// </summary>
    /// <param name="frame">Destination of at least <see cref="ShipStatusFrameBytes"/> bytes.</param>
    /// <param name="ship">The ship.</param>
    /// <param name="tick">Server tick of the status.</param>
    /// <param name="status">The status.</param>
    /// <returns>The frame length.</returns>
    /// <exception cref="ArgumentException">A short destination, no ship, a negative or non-finite value or an undefined launch block.</exception>
    public static int EncodeShipStatus(Span<byte> frame, EntityId ship, Tick tick, in ShipFlightStatus status)
    {
        if (frame.Length < ShipStatusFrameBytes || ship.IsNone || !IsStatusValue(status.Fuel) || !IsStatusValue(status.FuelCapacity)
            || !IsStatusValue(status.SpeedLimit) || !Enum.IsDefined(status.LaunchBlock))
        {
            throw new ArgumentException("Ship status needs a ship, non-negative finite values and a defined launch block.", nameof(status));
        }

        frame[0] = (byte)MessageType.ShipStatus;
        frame[1] = CurrentFormatVersion;
        BinaryPrimitives.WriteUInt64LittleEndian(frame[2..], ship.Value);
        BinaryPrimitives.WriteUInt64LittleEndian(frame[10..], tick.Value);
        BinaryPrimitives.WriteSingleLittleEndian(frame[18..], status.Fuel);
        BinaryPrimitives.WriteSingleLittleEndian(frame[22..], status.FuelCapacity);
        BinaryPrimitives.WriteSingleLittleEndian(frame[26..], status.SpeedLimit);
        frame[30] = (byte)status.Flags;
        frame[31] = (byte)status.LaunchBlock;
        return ShipStatusFrameBytes;
    }

    // K5-26 decision 8: [Ship u64][Tick u64][Fuel f32][FuelCapacity f32][SpeedLimit f32][Flags u8][LaunchBlock u8].
    private static void WriteShipStatus(ByteWriter w, ShipStatus status)
    {
        if (status.Ship.IsNone || !IsStatusValue(status.Fuel) || !IsStatusValue(status.FuelCapacity) || !IsStatusValue(status.SpeedLimit)
            || !Enum.IsDefined(status.LaunchBlock))
        {
            throw new ArgumentException("Ship status needs a ship, non-negative finite values and a defined launch block.", nameof(status));
        }

        w.WriteUInt64(status.Ship.Value);
        w.WriteUInt64(status.Tick.Value);
        w.WriteSingle(status.Fuel);
        w.WriteSingle(status.FuelCapacity);
        w.WriteSingle(status.SpeedLimit);
        w.WriteByte((byte)status.Flags);
        w.WriteByte((byte)status.LaunchBlock);
    }

    private static ShipStatus ReadShipStatus(ref ByteReader r)
    {
        var ship = new EntityId(r.ReadUInt64());
        var tick = new Tick(r.ReadUInt64());
        float fuel = r.ReadSingle();
        float capacity = r.ReadSingle();
        float limit = r.ReadSingle();
        var flags = (ShipFlightFlags)r.ReadByte();
        var block = (ShipLaunchBlock)r.ReadByte();
        if (ship.IsNone || !IsStatusValue(fuel) || !IsStatusValue(capacity) || !IsStatusValue(limit) || !Enum.IsDefined(block))
        {
            throw new ProtocolViolationException("Invalid ship status.");
        }

        return new ShipStatus(ship, tick, fuel, capacity, limit, flags, block);
    }

    private static bool IsStatusValue(float v) => float.IsFinite(v) && v >= 0f;

    private static void WriteContainerState(ByteWriter w, ContainerState state)
    {
        if (!CoreCodecPrimitives.IsKnownRejectReason((byte)state.Reason))
        {
            throw new ArgumentException($"Invalid RejectReason {(byte)state.Reason}.", nameof(state));
        }

        w.WriteVarUInt(state.Container.Value);
        w.WriteByte((byte)state.Reason);
        if (state.Reason != RejectReason.None)
        {
            if (state.Kind != 0 || state.Access != 0 || state.Size != 0 || state.Version != 0 || state.Slots.Count != 0)
            {
                throw new ArgumentException("A refused ContainerState carries no fields after the reason.", nameof(state));
            }

            return;
        }

        if (!Enum.IsDefined(state.Kind) || !Enum.IsDefined(state.Access))
        {
            throw new ArgumentException($"Invalid ContainerKind {(byte)state.Kind} or ContainerAccess {(byte)state.Access}.", nameof(state));
        }

        if (state.Size < 1 || state.Size > ProtocolConstants.MaxContainerSlots)
        {
            throw new ArgumentException($"ContainerState size {state.Size} must be in 1..{ProtocolConstants.MaxContainerSlots}.", nameof(state));
        }

        w.WriteByte((byte)state.Kind);
        w.WriteByte((byte)state.Access);
        w.WriteVarUInt(state.Size);
        w.WriteVarUInt(state.Version);
        WriteSlotContents(w, state.Slots, state.Size, nameof(state));
    }

    private static ContainerState ReadContainerState(ref ByteReader r)
    {
        var container = new ContainerId(r.ReadVarUInt());
        byte reasonByte = r.ReadByte();
        if (!CoreCodecPrimitives.IsKnownRejectReason(reasonByte))
        {
            throw new ProtocolViolationException($"Unknown reject reason {reasonByte}.");
        }

        if (reasonByte != (byte)RejectReason.None)
        {
            return ContainerState.Rejected(container, (RejectReason)reasonByte);
        }

        var kind = (ContainerKind)CoreCodecPrimitives.ReadEnumByte(ref r, (byte)ContainerKind.PlayerBag, (byte)ContainerKind.DroppedItem, "container kind");
        var access = (ContainerAccess)CoreCodecPrimitives.ReadEnumByte(ref r, (byte)ContainerAccess.Shared, (byte)ContainerAccess.Private, "container access");
        ushort size = r.ReadVarUInt16();
        if (size < 1 || size > ProtocolConstants.MaxContainerSlots)
        {
            throw new ProtocolViolationException($"ContainerState size {size} must be in 1..{ProtocolConstants.MaxContainerSlots}.");
        }

        uint version = r.ReadVarUInt32();
        var slots = ReadSlotContents(ref r, size);
        return new ContainerState(container, RejectReason.None, kind, access, size, version, slots);
    }

    /// <summary>Count, then (slot, stack) with slots strictly ascending and below <paramref name="slotLimit"/>; an empty stack is type 0.</summary>
    private static void WriteSlotContents(ByteWriter w, IReadOnlyList<SlotContent> slots, int slotLimit, string paramName)
    {
        if (slots.Count > slotLimit)
        {
            throw new ArgumentException($"{slots.Count} slots exceed the limit {slotLimit}.", paramName);
        }

        w.WriteVarUInt((ulong)slots.Count);
        int previous = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.Slot <= previous || slot.Slot >= slotLimit)
            {
                throw new ArgumentException($"Slot {slot.Slot} is not ascending or not below {slotLimit}.", paramName);
            }

            previous = slot.Slot;
            w.WriteVarUInt(slot.Slot);
            if (slot.Stack.IsEmpty)
            {
                if (slot.Stack.Count != 0 || slot.Stack.Instance is not null)
                {
                    throw new ArgumentException($"Empty slot {slot.Slot} has a count or instance data.", paramName);
                }

                w.WriteVarUInt(0);
            }
            else
            {
                CoreCodecPrimitives.WriteItemStack(w, slot.Stack);
            }
        }
    }

    private static SlotContent[] ReadSlotContents(ref ByteReader r, int slotLimit)
    {
        ulong count = r.ReadVarUInt();
        if (count > (ulong)slotLimit)
        {
            throw new ProtocolViolationException($"{count} slots exceed the limit {slotLimit}.");
        }

        var slots = new SlotContent[(int)count];
        int previous = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            ushort slot = r.ReadVarUInt16();
            if (slot <= previous || slot >= slotLimit)
            {
                throw new ProtocolViolationException($"Slot {slot} is not ascending or not below {slotLimit}.");
            }

            previous = slot;

            // An empty slot is the single type 0; otherwise the stack is read again from its type.
            var stackStart = r;
            if (r.ReadVarUInt16() == 0)
            {
                slots[i] = new SlotContent(slot, ItemStack.Empty);
            }
            else
            {
                r = stackStart;
                slots[i] = new SlotContent(slot, CoreCodecPrimitives.ReadItemStack(ref r));
            }
        }

        return slots;
    }

    private static void WriteEntityState(ByteWriter w, EntityState state, int maxFrames)
    {
        if (state.FrameIndex >= maxFrames)
        {
            throw new ArgumentException($"EntityState FrameIndex {state.FrameIndex} is outside frame table of size {maxFrames}.", nameof(state));
        }

        if ((state.Fields & ~(EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.Velocity | EntityStateFields.MotionFlags)) != 0)
        {
            throw new ArgumentException($"Invalid EntityStateFields {(byte)state.Fields}.", nameof(state));
        }

        w.WriteVarUInt(state.Entity.Value);
        w.WriteByte((byte)state.Fields);
        w.WriteByte(state.FrameIndex);
        w.WriteVarUInt(state.ParentSeq);

        if ((state.Fields & EntityStateFields.Position) != 0)
        {
            w.WriteVector3(state.Position);
        }

        if ((state.Fields & EntityStateFields.Rotation) != 0)
        {
            uint packedRot = Quantization.PackRotation(state.Rotation);
            w.WriteUInt32(packedRot);
        }

        if ((state.Fields & EntityStateFields.Velocity) != 0)
        {
            w.WriteHalf(state.Velocity.X);
            w.WriteHalf(state.Velocity.Y);
            w.WriteHalf(state.Velocity.Z);
        }

        if ((state.Fields & EntityStateFields.MotionFlags) != 0)
        {
            w.WriteByte(state.MotionFlags);
        }
    }

    private static EntityState ReadEntityState(ref ByteReader r, int maxFrames)
    {
        var entity = new EntityId(r.ReadVarUInt());
        byte fieldsByte = r.ReadByte();
        if ((fieldsByte & ~0x0F) != 0)
        {
            throw new ProtocolViolationException($"EntityStateFields contains bits outside 0x0F mask ({fieldsByte}).");
        }

        var fields = (EntityStateFields)fieldsByte;
        byte frameIndex = r.ReadByte();
        if (frameIndex >= maxFrames)
        {
            throw new ProtocolViolationException($"EntityState FrameIndex {frameIndex} is outside frame table of size {maxFrames}.");
        }

        uint parentSeq = r.ReadVarUInt32();

        Vector3 position = default;
        Quaternion rotation = Quaternion.Identity;
        Vector3 velocity = default;
        byte motionFlags = 0;

        if ((fields & EntityStateFields.Position) != 0)
        {
            position = r.ReadVector3();
        }

        if ((fields & EntityStateFields.Rotation) != 0)
        {
            rotation = Quantization.UnpackRotation(r.ReadUInt32());
        }

        if ((fields & EntityStateFields.Velocity) != 0)
        {
            velocity = new Vector3(r.ReadHalf(), r.ReadHalf(), r.ReadHalf());
        }

        if ((fields & EntityStateFields.MotionFlags) != 0)
        {
            motionFlags = r.ReadByte();
        }

        return new EntityState(entity, fields, frameIndex, parentSeq, position, rotation, velocity, motionFlags);
    }

    private static bool ValidTerrainSyncHeader(uint transfer, byte purpose, PlanetId planet, OpId barrier, bool last, int count) =>
        transfer != 0 && purpose <= 2 && planet.Value != 0 && (purpose == 2 ? barrier.Value != 0 : barrier.Value == 0)
        && count <= ProtocolConstants.MaxTerrainSyncEntries && (!last || count == 0);

    private static void ValidateTerrainSync(TerrainSync sync)
    {
        if (!ValidTerrainSyncHeader(sync.TransferId, sync.Purpose, sync.Planet, sync.BarrierOpId, sync.IsLastPage, sync.Entries.Count))
        {
            throw new ArgumentException("Invalid terrain manifest header.", nameof(sync));
        }
        for (var i = 0; i < sync.Entries.Count; i++)
        {
            var key = sync.Entries[i].Key;
            if (key.Planet != sync.Planet || key.Lod != 0 || (i > 0 && sync.Entries[i - 1].Key >= key))
            {
                throw new ArgumentException("Terrain manifest keys must be sorted distinct LOD zero keys of its planet.", nameof(sync));
            }
        }
    }

    private static bool ValidTerrainView(TerrainView view) => view.Planet.Value != 0 && view.ViewSeq != 0
        && float.IsFinite(view.VisualPosition.X) && float.IsFinite(view.VisualPosition.Y) && float.IsFinite(view.VisualPosition.Z)
        && float.IsFinite(view.CollisionPosition.X) && float.IsFinite(view.CollisionPosition.Y) && float.IsFinite(view.CollisionPosition.Z)
        && float.IsFinite(view.VisualDistance) && view.VisualDistance is >= 0 and <= 16384
        && float.IsFinite(view.CollisionDistance) && view.CollisionDistance is >= 0 and <= 16384;

    private static void WriteTerrainBlockKey(ByteWriter w, TerrainBlockKey key)
    {
        w.WriteVarUInt(key.Planet.Value);
        w.WriteByte(key.Lod);
        w.WriteVarInt(key.X);
        w.WriteVarInt(key.Y);
        w.WriteVarInt(key.Z);
    }

    private static TerrainBlockKey ReadTerrainBlockKey(ref ByteReader r)
    {
        var planet = new PlanetId(r.ReadVarUInt());
        byte lod = r.ReadByte();
        int x = r.ReadVarInt32();
        int y = r.ReadVarInt32();
        int z = r.ReadVarInt32();
        return new TerrainBlockKey(planet, lod, x, y, z);
    }

    private static void WriteTerrainBlockVersion(ByteWriter w, TerrainBlockVersion tbv)
    {
        WriteTerrainBlockKey(w, tbv.Key);
        w.WriteVarUInt(tbv.Version);
    }

    private static TerrainBlockVersion ReadTerrainBlockVersion(ref ByteReader r)
    {
        var key = ReadTerrainBlockKey(ref r);
        uint version = r.ReadVarUInt32();
        return new TerrainBlockVersion(key, version);
    }
}
