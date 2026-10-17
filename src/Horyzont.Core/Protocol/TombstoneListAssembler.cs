using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Core.Protocol;

/// <summary>
/// Collects the pages of the tombstone list of message 48 (K6-35 D21). The list is replaced only when every page of one revision has arrived, so a client never shows a
/// half list, whatever the order of the pages; a page of an older revision than the newest one seen is ignored.
/// </summary>
public sealed class TombstoneListAssembler
{
    private uint? _revision;
    private IReadOnlyList<TombstoneEntry> _list = [];
    private uint? _pendingRevision;
    private IReadOnlyList<TombstoneEntry>?[] _pages = [];
    private int _received;

    /// <summary>Gets the revision of the published list, or null before the first complete one.</summary>
    public uint? Revision => _revision;

    /// <summary>Gets the published list: empty before the first complete revision.</summary>
    public IReadOnlyList<TombstoneEntry> Tombstones => _list;

    /// <summary>Takes one page.</summary>
    /// <param name="page">The respawn part of a message 48.</param>
    /// <returns>True when this page completed a revision and the published list changed.</returns>
    public bool Offer(PlayerRespawnState page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.PageCount == 0 || page.Page >= page.PageCount)
        {
            return false;
        }

        if (_revision is { } published && !IsNewer(page.Revision, published))
        {
            return false;
        }

        if (_pendingRevision is { } pending && page.Revision != pending)
        {
            if (!IsNewer(page.Revision, pending))
            {
                return false;
            }

            _pendingRevision = null;
        }

        if (_pendingRevision is null || _pages.Length != page.PageCount)
        {
            // A new revision, or a page that disagrees about the page count: what was collected belongs to nothing.
            _pendingRevision = page.Revision;
            _pages = new IReadOnlyList<TombstoneEntry>?[page.PageCount];
            _received = 0;
        }

        if (_pages[page.Page] is null)
        {
            _received++;
        }

        _pages[page.Page] = page.Tombstones;
        if (_received < _pages.Length)
        {
            return false;
        }

        var all = new List<TombstoneEntry>();
        foreach (var part in _pages)
        {
            all.AddRange(part!);
        }

        _list = all;
        _revision = page.Revision;
        _pendingRevision = null;
        _pages = [];
        _received = 0;
        return true;
    }

    private static bool IsNewer(uint candidate, uint known) => unchecked((int)(candidate - known)) > 0;
}
