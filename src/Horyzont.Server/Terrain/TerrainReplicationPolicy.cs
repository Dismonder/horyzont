namespace Horyzont.Server.Terrain;
/// <summary>Bounded terrain replication selection policy; bandwidth belongs to the shared session budget.</summary>
public static class TerrainReplicationPolicy
{
    /// <summary>Maximum distinct queued keys per remote client.</summary>
    public const int MaxQueuedBlocks = 4096;
    /// <summary>Maximum data blocks sent to one client per frame.</summary>
    public const int MaxBlocksPerFrame = 2;
    /// <summary>Time after which old bulk work takes precedence over fresh work.</summary>
    public static TimeSpan AgingInterval => TimeSpan.FromSeconds(1);
}
