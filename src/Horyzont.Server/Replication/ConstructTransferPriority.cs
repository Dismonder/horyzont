namespace Horyzont.Server.Replication;

/// <summary>
/// Order in which complete construct states start for one recipient (K5-26 decision 5): a transfer that has sent a part keeps going,
/// then the recipient's own ship, other ships nearest first, static constructs in arrival order.
/// </summary>
public enum ConstructTransferPriority : byte
{
    /// <summary>The ship in whose frame chain the recipient is.</summary>
    Own = 0,

    /// <summary>Another ship-mode construct, by distance in the region at enqueue time.</summary>
    Ship = 1,

    /// <summary>A static construct, first in first out (the K3-15 behaviour).</summary>
    Static = 2,
}
