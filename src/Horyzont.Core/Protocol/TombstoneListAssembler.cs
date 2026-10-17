using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Core.Protocol;

/// <summary>
/// Collects the pages of the tombstone list of message 48 (K6-35 D21). The list is replaced only when every page of one revision has arrived, so a client never shows a
/// half list, whatever the order of the pages; a page of an older revision than the newest one seen is ignored.
/// </summary>
public sealed class TombstoneListAssembler
{
    /// <summary>Gets the revision of the published list, or null before the first complete one.</summary>
    public uint? Revision => throw new NotImplementedException("K6-35 B3.");

    /// <summary>Gets the published list: empty before the first complete revision.</summary>
    public IReadOnlyList<TombstoneEntry> Tombstones => throw new NotImplementedException("K6-35 B3.");

    /// <summary>Takes one page.</summary>
    /// <param name="page">The respawn part of a message 48.</param>
    /// <returns>True when this page completed a revision and the published list changed.</returns>
    public bool Offer(PlayerRespawnState page) => throw new NotImplementedException("K6-35 B3.");
}
