namespace Horyzont.Core.Protocol;

/// <summary>Limits of the network protocol and of the persisted formats.</summary>
public static class ProtocolConstants
{
    /// <summary>Protocol version; bumped by every change of a message format. 2: RejectReason.ServerReadOnly (K2-10). 3: InputButtons.DevFly and the 0x3F button mask (K2-11).</summary>
    public const ushort ProtocolVersion = 5;

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
}
