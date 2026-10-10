namespace Horyzont.Server.Replication;

/// <summary>Reliable replication traffic classes.</summary>
public enum ReplicationTraffic : byte
{
    /// <summary>Live changes; always sent and charged.</summary>
    Live = 0,
    /// <summary>Initial state and repairs; wait for available tokens.</summary>
    Bulk = 1,
}
