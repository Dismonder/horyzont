namespace Horyzont.Server.Terrain;

/// <summary>Bounded terrain replication selection policy; bandwidth belongs to the shared session budget.</summary>
public static class TerrainReplicationPolicy
{
    /// <summary>Maximum distinct queued keys per remote client.</summary>
    public const int MaxQueuedBlocks = 4096;

    /// <summary>
    /// Maximum data blocks sent to one client per server tick. The shared byte budget is the real limit; the count only bounds the
    /// work of one tick (an edit of the standard tool touches up to eight blocks and must not wait four ticks for them).
    /// </summary>
    public const int MaxBlocksPerFrame = 16;

    /// <summary>Largest cache report the server accepts per planet and client; the client skips the rest, farthest first.</summary>
    public const int MaxReportKeysPerPlanet = 65_536;

    /// <summary>Largest viewer distance a client may declare, in metres.</summary>
    public const float MaxViewDistance = 16_384f;

    /// <summary>How far the declared viewer positions may be from the feet of the server character, in metres.</summary>
    public const float ViewerTolerance = 64f;

    /// <summary>
    /// Distance from the centre of a planet, in planet radii, up to which a player may view its terrain (K5-22 decision 8: the client
    /// activates a terrain from 4 R and releases it beyond 4.4 R).
    /// </summary>
    public const float OtherPlanetViewFactor = 4.4f;

    /// <summary>Radius around the collision viewer inside which edit data is sent as live traffic, in metres.</summary>
    public const float LiveRadius = 64f;

    /// <summary>Block exports (buffer reads or generator runs) per server tick, over all clients.</summary>
    public const int MaxExportsPerTick = 32;

    /// <summary>Generator digest checks per server tick, over all clients.</summary>
    public const int MaxChecksumChecksPerTick = 2;

    /// <summary>Entries of one checksum message the server examines.</summary>
    public const int MaxChecksumEntries = 32;

    /// <summary>How far from the collision viewer a block may be to be checked, in metres.</summary>
    public const float ChecksumRadius = 256f;

    /// <summary>Time after which old bulk work takes precedence over fresh work.</summary>
    public static TimeSpan AgingInterval => TimeSpan.FromSeconds(1);

    /// <summary>Shortest interval between two checksum messages of one client.</summary>
    public static TimeSpan ChecksumWindow => TimeSpan.FromSeconds(4);
}
