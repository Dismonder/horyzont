using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence;

/// <summary>
/// A connection to a world file. Opens without pooling, so disposing really closes the file and rolls back an open
/// transaction, and applies the pragmas: WAL, <c>synchronous=FULL</c>, foreign keys, busy timeout and in-memory temp store.
/// </summary>
public sealed class WorldDatabase : IDisposable
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

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
    /// <returns>The database.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="WorldCorruptedException">The file is not a readable SQLite database.</exception>
    public static WorldDatabase Open(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The world file does not exist.", fullPath);
        }

        return OpenCore(fullPath, SqliteOpenMode.ReadWrite);
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

    private static WorldDatabase OpenCore(string fullPath, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(ConnectionString(fullPath, mode, pooling: false));
        try
        {
            connection.Open();
            ApplyPragmas(connection, readOnly: false);
        }
        catch (SqliteException e) when (IsCorruption(e))
        {
            connection.Dispose();
            throw new WorldCorruptedException($"'{fullPath}' is not a readable world database.", e);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return new WorldDatabase(fullPath, connection);
    }
}
