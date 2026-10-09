using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Backup;

/// <summary>
/// Copies a world with the SQLite Online Backup API to <c>&lt;directory&gt;/&lt;yyyyMMdd-HHmmss&gt;-&lt;sequence&gt;-&lt;reason&gt;.db</c>
/// (UTC, sequence of six or more digits) and keeps the newest copies. The sequence, one more than the highest published copy, is the
/// creation order: the clock and the reason never decide which copy is the oldest. A copy is written under a temporary name and
/// renamed only after it is complete and closed, so an interrupted copy is never listed or kept. A copy is a single file in
/// rollback-journal mode.
/// </summary>
public sealed partial class BackupService
{
    /// <summary>Suffix of a copy that is still being written; such files are removed by the next copy.</summary>
    public const string TemporarySuffix = ".partial";

    private readonly string _directory;
    private readonly int _keep;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    /// <param name="backupsDirectory">Directory of the copies; created on the first copy.</param>
    /// <param name="keep">Number of newest copies kept, at least 1.</param>
    /// <param name="time">Clock of the file names.</param>
    public BackupService(string backupsDirectory, int keep, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(backupsDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        ArgumentNullException.ThrowIfNull(time);
        _directory = Path.GetFullPath(backupsDirectory);
        _keep = keep;
        _time = time;
    }

    /// <summary>Test hook: called on the copying thread once the source connection is open, immediately before the copy starts.</summary>
    internal Action? BeforeCopy { get; set; }

    /// <summary>Test hook: called with the temporary path after the copy is closed and before it is published.</summary>
    internal Action<string>? BeforePublish { get; set; }

    /// <summary>Copies the database of <paramref name="source"/> and removes the oldest copies above the limit.</summary>
    /// <param name="source">Open connection of the world; no transaction may be open on it.</param>
    /// <param name="reason">Short reason in the file name (letters, digits and '-').</param>
    /// <returns>Path of the copy; it is always among the kept copies.</returns>
    public string CreateBackup(SqliteConnection source, string reason)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateReason(reason);
        Directory.CreateDirectory(_directory);
        RemoveTemporaryCopies();
        var published = Published();
        var sequence = published.Count == 0 ? 1 : published[^1].Sequence + 1;
        var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path;
        while (true)
        {
            path = Path.Combine(_directory, $"{stamp}-{sequence.ToString("D6", CultureInfo.InvariantCulture)}-{reason}.db");
            if (!File.Exists(path))
            {
                break;
            }

            sequence++;
        }

        var temporary = path + TemporarySuffix;
        try
        {
            using (var destination = new SqliteConnection(WorldDatabase.ConnectionString(temporary, SqliteOpenMode.ReadWriteCreate, pooling: false)))
            {
                destination.Open();
                BeforeCopy?.Invoke();
                source.BackupDatabase(destination);
                WorldDatabase.Execute(destination, "PRAGMA journal_mode = DELETE;");
            }

            BeforePublish?.Invoke(temporary);

            // Atomic on one volume: the copy appears under its final name only when complete.
            File.Move(temporary, path, overwrite: false);
        }
        catch
        {
            Sql.DeleteWithSideFiles(temporary);
            File.Delete(temporary + "-journal");
            throw;
        }

        Rotate(path);
        return path;
    }

    /// <summary>Checks a backup reason: not empty, only ASCII letters, digits and '-'.</summary>
    /// <param name="reason">The reason.</param>
    /// <exception cref="ArgumentException">The reason is empty or has another character.</exception>
    internal static void ValidateReason(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        foreach (var c in reason)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                throw new ArgumentException("The reason may contain only ASCII letters, digits and '-'.", nameof(reason));
            }
        }
    }

    /// <summary>Published copies of the directory, oldest first (by creation sequence).</summary>
    /// <returns>Full paths.</returns>
    public IReadOnlyList<string> List() => [.. Published().Select(copy => copy.Path)];

    [GeneratedRegex(@"^\d{8}-\d{6}-(?<sequence>\d{6,18})-[A-Za-z0-9-]+\.db$", RegexOptions.CultureInvariant)]
    private static partial Regex CopyName();

    private List<(long Sequence, string Path)> Published()
    {
        var copies = new List<(long Sequence, string Path)>();
        if (!Directory.Exists(_directory))
        {
            return copies;
        }

        foreach (var file in Directory.EnumerateFiles(_directory))
        {
            var match = CopyName().Match(Path.GetFileName(file));
            if (match.Success)
            {
                copies.Add((long.Parse(match.Groups["sequence"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture), file));
            }
        }

        copies.Sort(static (a, b) => a.Sequence != b.Sequence ? a.Sequence.CompareTo(b.Sequence) : string.CompareOrdinal(a.Path, b.Path));
        return copies;
    }

    /// <summary>Copies left by a process that died while writing them; this service is their only writer.</summary>
    private void RemoveTemporaryCopies()
    {
        foreach (var file in Directory.EnumerateFiles(_directory, "*" + TemporarySuffix + "*"))
        {
            File.Delete(file);
        }
    }

    private void Rotate(string created)
    {
        var copies = Published();
        var excess = copies.Count - _keep;
        foreach (var copy in copies)
        {
            if (excess <= 0)
            {
                break;
            }

            if (!string.Equals(copy.Path, created, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(copy.Path);
                excess--;
            }
        }
    }
}
