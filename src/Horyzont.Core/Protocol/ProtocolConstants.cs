namespace Horyzont.Core.Protocol;

/// <summary>Limits of the network protocol and of the persisted formats.</summary>
public static class ProtocolConstants
{
    /// <summary>Protocol version; bumped by every change of a message format. 2: RejectReason.ServerReadOnly (K2-10). 3: InputButtons.DevFly and the 0x3F button mask (K2-11). 4: K3-15 replication messages. 5: K3-16 terrain manifest sync. 6: ServerStatus/JoinUnavailable (K3-18). 7: ShipModeSetOp, ShipModeIntent and reject reasons 30, 31, 37 (K5-23). 8: ship-mode constructs in snapshots (K5-20; main 7 after K5-23 + 1 per D4). 9: StarSystemState message 32 and checkpoint blob 10 (K5-21; main 8 after K5-20 + 1 per D4). 10: TerrainView of a planet other than the region of the player accepted up to 4.4 R (K5-22; main 9 after K5-21 + 1 per D4). 11: ShipFuelAddOp operation 21, RefuelIntent intent 13, reject reason 39 (ShipNotParked) and checkpoint blob 12 (ship state) (K5-24; main 10 after K5-22 + 1 per D4). 12: MarkersState message 33, operations 22/23, intents 14/15, reject reason 36 and blob 11 (K5-27; main 11 after K5-24 + 1 per D4). 13: ShipStatus message 31 and the seat in a player's EntitySpawn.KindData (K5-26; main 12 after K5-27 + 1 per D4). 14: item intents 20-21, reject reasons 40-43 and messages 40-43, 49 (K6-29W). 15: operations 36, 37, 40, 44, 49, intents 22, 23, 28, 29, reject reasons 54 and 57, message 46 and blobs 21, 22 (K6-30). 16: operations 47, 48, intent 27, reject reason 53 and message 45 (K6-31). 17: operations 43, 45, station state 3 in operation 40 and message 46, intent 24, reject reasons 44 to 46, message 48, blob 22 version 2 and blob 25 (K6-32; K6-31 is 16 by D4).</summary>
    public const ushort ProtocolVersion = 17;

    /// <summary>Most records in a <see cref="Messages.MarkersState"/>: 256 shared and 256 own private markers (K5-27).</summary>
    public const int MaxMarkersInState = 512;

    /// <summary>
    /// Largest warm-up progress of the rest in message 44, in ticks: <c>RestWarmupTicks</c> of 20 s at 60 ticks per second (K6-33 D22). The server clamps
    /// the values to the maxima before sending.
    /// </summary>
    public const int MaxRestWarmupTicks = 1200;

    /// <summary>
    /// Largest remaining time of the rest in message 44, in ticks: (8 + 10) minutes at 60 ticks per second (K6-33 D22). The server clamps the values to
    /// the maxima before sending.
    /// </summary>
    public const int MaxRestRemainingTicks = 64_800;

    /// <summary>Maximum entries per terrain manifest page (K3-16).</summary>
    public const int MaxTerrainSyncEntries = 1024;

    /// <summary>Bits of <see cref="Messages.InputButtons"/> that a valid input may set.</summary>
    public const int InputButtonsMask = 0x3F;

    /// <summary>Largest reliable frame: type, format version and payload.</summary>
    public const int MaxReliableMessageBytes = 256 * 1024;

    /// <summary>Largest unreliable frame.</summary>
    public const int MaxUnreliableMessageBytes = 1150;

    /// <summary>Largest display name in UTF-8 bytes.</summary>
    public const int MaxDisplayNameBytes = 32;

    /// <summary>Largest server password in UTF-8 bytes.</summary>
    public const int MaxPasswordBytes = 64;

    /// <summary>Largest platform authentication ticket.</summary>
    public const int MaxAuthTicketBytes = 1024;

    /// <summary>Largest handshake reject detail in UTF-8 bytes.</summary>
    public const int MaxRejectDetailBytes = 256;

    /// <summary>Largest registry key and generator profile in UTF-8 bytes.</summary>
    public const int MaxRegistryKeyBytes = 64;

    /// <summary>Most block types in a world.</summary>
    public const int MaxBlockTypes = 2048;

    /// <summary>Most terrain materials in a world.</summary>
    public const int MaxMaterials = 255;

    /// <summary>Most planets in <see cref="Messages.ServerHello"/>.</summary>
    public const int MaxPlanetsInHello = 64;

    /// <summary>Most inputs in one <see cref="Messages.InputBatch"/>.</summary>
    public const int MaxInputsPerBatch = 5;

    /// <summary>Most frames in one snapshot.</summary>
    public const int MaxFramesPerSnapshot = 8;

    /// <summary>
    /// Most entities in one snapshot: 22 full states and 8 frames fit in 1150 B, leaving 3 B per entity for combat fields
    /// (subproject 4).
    /// </summary>
    public const int MaxEntitiesPerSnapshot = 22;

    /// <summary>Largest KindData in <see cref="Messages.EntitySpawn"/>.</summary>
    public const int MaxEntityKindDataBytes = 64;

    /// <summary>Most construct changes of one operation.</summary>
    public const int MaxConstructChangesPerOperation = 1024;

    /// <summary>Most chunks in one <see cref="Messages.ConstructState"/> part (12 worst-case RLE chunks fit in 256 KiB).</summary>
    public const int MaxChunksPerConstructStatePart = 12;

    /// <summary>Most entries in a terrain cache report.</summary>
    public const int MaxTerrainCacheEntries = 4096;

    /// <summary>Most entries in a terrain checksum message.</summary>
    public const int MaxTerrainChecksumEntries = 1024;

    /// <summary>Largest terrain block payload.</summary>
    public const int MaxTerrainBlockDataBytes = 64 * 1024;

    /// <summary>Most item types in a world (<see cref="Ids.ItemTypeId"/> 1..4096).</summary>
    public const int MaxItemTypes = 4096;

    /// <summary>Most progress keys of a world (K6-32).</summary>
    public const int MaxProgressKeys = 256;

    /// <summary>Longest set of discovered item types in bytes: type ids are 1 to <see cref="MaxItemTypes"/>, so bit 4096 sits in byte 512 (K6-32).</summary>
    public const int MaxDiscoveredBytes = (MaxItemTypes / 8) + 1;

    /// <summary>Largest value of health or stamina in a record or a message (K6-33); a larger, negative or non-finite value is invalid.</summary>
    public const float MaxVitalValue = 10_000f;

    /// <summary>
    /// Most stations with time (furnace, refinery) in one construct (K6-32, review B1, 7): with this many the state message of a construct
    /// (<see cref="Messages.BlockEntityStates"/>, at most 113 678 bytes) still fits <see cref="MaxReliableMessageBytes"/>. A rule may lower it, never raise it.
    /// </summary>
    public const int MaxTimedStationsPerConstruct = 512;

    /// <summary>Longest recipe key in UTF-8 bytes (K6-32).</summary>
    public const int MaxRecipeKeyBytes = MaxRegistryKeyBytes;

    /// <summary>Most slots of one container.</summary>
    public const int MaxContainerSlots = 256;

    /// <summary>Most block states (<see cref="BlockEntities.BlockEntityState"/>) in one construct (K6-30).</summary>
    public const int MaxBlockEntitiesPerConstruct = 4096;

    /// <summary>Most regions in one <see cref="Messages.PropStates"/> (K6-31 D22).</summary>
    public const int MaxPropRegionsPerMessage = 64;

    /// <summary>Most harvested props in one <see cref="Messages.PropStates"/> (K6-31 D22).</summary>
    public const int MaxPropEntriesPerMessage = 4096;

    /// <summary>Largest teleport name in a <see cref="Intents.TeleportNameConfig"/>, UTF-8 bytes (K6-30 D23).</summary>
    public const int MaxTeleportNameBytes = 32;

    /// <summary>Most components of one <see cref="Operations.CompoundOp"/>.</summary>
    public const int MaxCompoundComponents = 4096;

    /// <summary>Largest encoded <see cref="Operations.CompoundOp"/> frame (kind, version, count and components).</summary>
    public const int MaxCompoundBytes = 160 * 1024;
}
