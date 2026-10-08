using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Backup;

/// <summary>
/// Copies a world with the SQLite Online Backup API to <c>&lt;directory&gt;/&lt;yyyyMMdd-HHmmss&gt;-&lt;reason&gt;.db</c> (UTC) and keeps the
/// newest copies. A copy is a single file in rollback-journal mode.
/// </summary>
public sealed class BackupService
{
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

    /// <summary>Copies the database of <paramref name="source"/> and removes the oldest copies above the limit.</summary>
    /// <param name="source">Open connection of the world; no transaction may be open on it.</param>
    /// <param name="reason">Short reason in the file name (letters, digits and '-').</param>
    /// <returns>Path of the copy.</returns>
    public string CreateBackup(SqliteConnection source, string reason)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(reason);
        foreach (var c in reason)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                throw new ArgumentException("The reason may contain only ASCII letters, digits and '-'.", nameof(reason));
            }
        }

        Directory.CreateDirectory(_directory);
        var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(_directory, $"{stamp}-{reason}.db");
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(_directory, $"{stamp}-{reason}-{suffix.ToString(CultureInfo.InvariantCulture)}.db");
        }

        try
        {
            using (var destination = new SqliteConnection(WorldDatabase.ConnectionString(path, SqliteOpenMode.ReadWriteCreate, pooling: false)))
            {
                destination.Open();
                source.BackupDatabase(destination);
                WorldDatabase.Execute(destination, "PRAGMA journal_mode = DELETE;");
            }
        }
        catch
        {
            Sql.DeleteWithSideFiles(path);
            throw;
        }

        Rotate();
        return path;
    }

    /// <summary>Copies of the directory, oldest first.</summary>
    /// <returns>Full paths.</returns>
    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var files = Directory.GetFiles(_directory, "*.db");
        // Names start with the UTC stamp; comparing without ".db" keeps "x" before its collision copy "x-2".
        Array.Sort(files, static (a, b) => string.CompareOrdinal(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b)));
        return files;
    }

    private void Rotate()
    {
        var files = List();
        for (var i = 0; i < files.Count - _keep; i++)
        {
            File.Delete(files[i]);
        }
    }
}
