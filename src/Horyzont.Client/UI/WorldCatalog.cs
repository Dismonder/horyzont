namespace Horyzont.Client.UI;

/// <summary>Lists one level of save directories without opening SQLite or touching journals.</summary>
public sealed class WorldCatalog
{
    /// <summary>Maximum number of displayed entries.</summary>
    public const int MaxEntries = 1024;
    /// <summary>Gets whether the most recent scan exceeded the entry limit.</summary>
    public bool LimitExceeded { get; private set; }

    /// <summary>Scans file metadata on a worker. The caller marshals the result to its UI thread.</summary>
    public Task<IReadOnlyList<WorldListEntry>> ListAsync(string savesDirectory, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savesDirectory);
        return Task.Run<IReadOnlyList<WorldListEntry>>(() => Scan(savesDirectory, ct), ct);
    }

    private IReadOnlyList<WorldListEntry> Scan(string savesDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LimitExceeded = false;
        if (!Directory.Exists(savesDirectory))
        {
            return Array.Empty<WorldListEntry>();
        }
        var entries = new List<WorldListEntry>(MaxEntries);
        foreach (string directory in Directory.EnumerateDirectories(savesDirectory))
        {
            ct.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "D", out var id))
            {
                continue;
            }
            WorldListEntry entry;
            try
            {
                var info = new DirectoryInfo(directory);
                // Do not follow links outside the save root.
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }
                var database = new FileInfo(Path.Combine(directory, "world.db"));
                bool present = database.Exists;
                entry = new(id, present ? database.LastWriteTimeUtc : info.LastWriteTimeUtc, present, null);
            }
            catch (IOException e)
            {
                // A bad individual entry must not hide the other worlds; retain its diagnostic for the owner.
                entry = new(id, DateTimeOffset.MinValue, false, e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                // Listing another world's metadata can still succeed; no database is opened here.
                entry = new(id, DateTimeOffset.MinValue, false, e.Message);
            }
            int index = entries.BinarySearch(entry, EntryComparer.Instance);
            entries.Insert(index < 0 ? ~index : index, entry);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveAt(MaxEntries);
                LimitExceeded = true;
            }
        }
        return entries;
    }

    private sealed class EntryComparer : IComparer<WorldListEntry>
    {
        public static EntryComparer Instance { get; } = new();
        public int Compare(WorldListEntry x, WorldListEntry y)
        {
            int date = y.LastWriteUtc.CompareTo(x.LastWriteUtc);
            return date != 0 ? date : x.WorldId.CompareTo(y.WorldId);
        }
    }
}
