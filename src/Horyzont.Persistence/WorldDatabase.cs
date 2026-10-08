using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence;

/// <summary>
/// A connection to a world file. Opens without pooling, so disposing really closes the file and rolls back an open
/// transaction, and applies the pragmas: WAL, <c>synchronous=FULL</c>, foreign keys, busy timeout and in-memory temp store.
/// </summary>
public sealed class WorldDatabase : IDisposable
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;
    private const int SqliteIoErr = 10;

    // Extended IOERR codes that are plausibly a lock conflict with another process (a scanner or indexer that has just
    // opened or mapped the file, or a handle of the killed process that is still being torn down), so a pause helps:
    // - TRUNCATE (1546): SetEndOfFile fails with ERROR_USER_MAPPED_FILE while another process has the file mapped (the
    //   case seen on CI: recovery truncating a stale -shm); SQLite's own retry loop does not cover it.
    // - SHMOPEN (4618), SHMSIZE (4874): the same conflict while opening or resizing the -shm file.
    // - LOCK (3850), ACCESS (3338), DELETE (2570): sharing violations on lock, probe and delete of the -wal/-shm files.
    // Deliberately absent: READ, WRITE, FSYNC, NOMEM, CORRUPTFS and the like, which are real faults, and CORRUPT/NOTADB,
    // which are damage; retrying them only delays the error.
    private static readonly int[] _transientIoExtendedCodes = [1546, 4618, 4874, 3850, 3338, 2570];

    // Total budget about 1.6 s; the observed conflict cleared within 250 ms.
    private static readonly TimeSpan[] _retryDelays =
    [
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800),
    ];

    private WorldDatabase(string path, SqliteConnection connection)
    {
        Path = path;
        Connection = connection;
    }

    /// <summary>Full path of the world file.</summary>
    public string Path { get; }

    /// <summary>The connection; used by one thread at a time.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>Schema version stored in <c>PRAGMA user_version</c>.</summary>
    public int UserVersion => ReadUserVersion(Connection);

    /// <summary>Opens an existing world file and applies the pragmas.</summary>
    /// <param name="path">World file.</param>
    /// <param name="logger">Logs each retry of a transient Windows I/O error; optional.</param>
    /// <returns>The database.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="WorldCorruptedException">The file is not a readable SQLite database.</exception>
    public static WorldDatabase Open(string path, ILogger? logger = null)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The world file does not exist.", fullPath);
        }

        return OpenCore(fullPath, SqliteOpenMode.ReadWrite, logger);
    }

    /// <summary>Runs <c>PRAGMA quick_check</c>.</summary>
    /// <returns>The result; <c>ok</c> for a sound file.</returns>
    public string QuickCheck()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        using var reader = command.ExecuteReader();
        var lines = new List<string>();
        while (reader.Read())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }

    /// <inheritdoc />
    public void Dispose() => Connection.Dispose();

    /// <summary>Creates a new, empty database file.</summary>
    /// <exception cref="IOException">The file already exists.</exception>
    internal static WorldDatabase CreateNew(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new IOException($"The world file '{fullPath}' already exists.");
        }

        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return OpenCore(fullPath, SqliteOpenMode.ReadWriteCreate);
    }

    internal static string ConnectionString(string fullPath, SqliteOpenMode mode, bool pooling) => new SqliteConnectionStringBuilder
    {
        DataSource = fullPath,
        Mode = mode,
        Pooling = pooling,
    }.ToString();

    /// <summary>Applies the per-connection pragmas; WAL is a property of the file and is set only by writers.</summary>
    internal static void ApplyPragmas(SqliteConnection connection, bool readOnly)
    {
        if (!readOnly)
        {
            Execute(connection, "PRAGMA journal_mode = WAL;");
        }

        Execute(connection, "PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA temp_store = MEMORY;");
    }

    /// <summary>True for an I/O error that is plausibly a short lock conflict with another process.</summary>
    internal static bool IsTransientIo(SqliteException exception) =>
        exception.SqliteErrorCode == SqliteIoErr && Array.IndexOf(_transientIoExtendedCodes, exception.SqliteExtendedErrorCode) >= 0;

    /// <summary>
    /// Opens a connection and applies the pragmas, retrying with backoff (about 1.6 s in total) while a transient I/O error
    /// persists; every failed attempt disposes its connection. After the budget the last SQLite error is thrown.
    /// </summary>
    internal static SqliteConnection OpenConnection(string connectionString, bool readOnly, ILogger? logger = null)
    {
        var path = new SqliteConnectionStringBuilder(connectionString).DataSource;
        for (var attempt = 0; ; attempt++)
        {
            var connection = new SqliteConnection(connectionString);
            try
            {
                connection.Open();
                ApplyPragmas(connection, readOnly);
                return connection;
            }
            catch (SqliteException e) when (IsTransientIo(e) && attempt < _retryDelays.Length)
            {
                connection.Dispose();
                var delay = _retryDelays[attempt];
                if (logger is not null)
                {
                    PersistenceLog.OpenRetried(logger, path, attempt + 1, e.SqliteExtendedErrorCode, (int)delay.TotalMilliseconds);
                }

                Thread.Sleep(delay);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }

    /// <summary>True for SQLite errors that mean a damaged file.</summary>
    internal static bool IsCorruption(SqliteException exception) =>
        exception.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase;

    internal static int ReadUserVersion(SqliteConnection connection, SqliteTransaction? transaction = null) =>
        Convert.ToInt32(ExecuteScalar(connection, "PRAGMA user_version;", transaction), CultureInfo.InvariantCulture);

    internal static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static object? ExecuteScalar(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static WorldDatabase OpenCore(string fullPath, SqliteOpenMode mode, ILogger? logger = null)
    {
        try
        {
            var connection = OpenConnection(ConnectionString(fullPath, mode, pooling: false), readOnly: false, logger);
            return new WorldDatabase(fullPath, connection);
        }
        catch (SqliteException e) when (IsCorruption(e))
        {
            throw new WorldCorruptedException($"'{fullPath}' is not a readable world database.", e);
        }
    }
}
