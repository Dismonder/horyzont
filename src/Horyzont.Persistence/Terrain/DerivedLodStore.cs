using System.Threading.Channels;

using Horyzont.Core.World.Terrain;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence.Terrain;

/// <summary>Disposable derived terrain database; a bounded writer persists immutable, fingerprinted mipmaps.</summary>
public sealed class DerivedLodStore
{
    private const int MaxEntries = 8192;
    private readonly Lock _lock = new();
    private readonly Dictionary<TerrainBlockKey, Entry> _entries = [];
    private readonly string _connectionString;
    private readonly TimeProvider _time;
    private readonly ILogger<DerivedLodStore> _logger;
    private readonly Channel<Pending> _writes = Channel.CreateBounded<Pending>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly Task _writer;
    private bool _closing;
    private bool _diskAvailable = true;

    /// <summary>Opens a separate schema-one derived database; never opens world.db.</summary>
    /// <param name="databasePath">Derived database path.</param>
    /// <param name="time">Clock for the five-second drain deadline.</param>
    /// <param name="logger">Fault logger.</param>
    public DerivedLodStore(string databasePath, TimeProvider time, ILogger<DerivedLodStore> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _time = time;
        _logger = logger;
        var path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS meta(schema INTEGER NOT NULL); "
                + "INSERT INTO meta SELECT 1 WHERE NOT EXISTS(SELECT 1 FROM meta); "
                + "CREATE TABLE IF NOT EXISTS derived(planet INTEGER,lod INTEGER,x INTEGER,y INTEGER,z INTEGER,fingerprint TEXT NOT NULL,data BLOB NOT NULL,"
                + "PRIMARY KEY(planet,lod,x,y,z));";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT schema FROM meta;";
            if ((long)command.ExecuteScalar()! != 1)
            {
                throw new InvalidDataException("Unsupported derived terrain schema.");
            }
            command.CommandText = "SELECT planet,lod,x,y,z,fingerprint,data FROM derived ORDER BY rowid DESC LIMIT 8192;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = new TerrainBlockKey(new Core.Ids.PlanetId(unchecked((ulong)reader.GetInt64(0))),
                    checked((byte)reader.GetInt64(1)), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
                _entries[key] = new Entry(reader.GetString(5), (byte[])reader.GetValue(6));
            }
        }
        catch (SqliteException exception)
        {
            // Derived data is rebuildable; memory remains usable without mutating the server world.
            _diskAvailable = false;
            DerivedStoreLog.DiskUnavailable(_logger, exception);
        }
        _writer = Task.Run(WriteAsync);
    }

    /// <summary>Publishes a copied blob before asynchronously persisting it; false means the bounded writer is full.</summary>
    /// <param name="key">LOD greater than zero key.</param>
    /// <param name="fingerprint">Profile, semantics and current child fingerprints.</param>
    /// <param name="blob">Immutable canonical envelope.</param>
    /// <returns>Whether publication was accepted.</returns>
    public bool Put(TerrainBlockKey key, string fingerprint, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentException.ThrowIfNullOrEmpty(fingerprint);
        if (key.Lod == 0)
        {
            throw new ArgumentException("Derived data requires LOD greater than zero.", nameof(key));
        }
        var entry = new Entry(fingerprint, (byte[])blob.Clone());
        lock (_lock)
        {
            if (_closing || (_diskAvailable && !_writes.Writer.TryWrite(new Pending(key, entry))))
            {
                return false;
            }
            if (_entries.Count == MaxEntries && !_entries.ContainsKey(key))
            {
                using var iterator = _entries.Keys.GetEnumerator();
                iterator.MoveNext();
                _entries.Remove(iterator.Current);
            }
            _entries[key] = entry;
            return true;
        }
    }

    /// <summary>Returns data only for the exact current fingerprint, including after restart.</summary>
    /// <param name="key">Derived key.</param>
    /// <param name="fingerprint">Current profile, semantics and children.</param>
    /// <param name="blob">Copied envelope.</param>
    /// <returns>Whether a current entry exists.</returns>
    public bool TryLoad(TerrainBlockKey key, string fingerprint, out byte[] blob)
    {
        lock (_lock)
        {
            if (!_closing && _entries.TryGetValue(key, out var entry) && entry.Fingerprint == fingerprint)
            {
                blob = (byte[])entry.Blob.Clone();
                return true;
            }
        }
        blob = [];
        return false;
    }

    /// <summary>Stops new writes and drains before closing the writer's database; timeout leaves the active writer alone.</summary>
    /// <returns>True after drain; false on the five-second injected deadline.</returns>
    public async Task<bool> CloseAsync()
    {
        lock (_lock)
        {
            _closing = true;
            _writes.Writer.TryComplete();
        }
        try
        {
            await _writer.WaitAsync(TimeSpan.FromSeconds(5), _time).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException exception)
        {
            // The writer still owns its connection and will dispose it only after finishing, never under an active command.
            DerivedStoreLog.DrainTimedOut(_logger, exception);
            return false;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private async Task WriteAsync()
    {
        if (!_diskAvailable)
        {
            return;
        }
        try
        {
            using var connection = Open();
            await foreach (var pending in _writes.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT OR REPLACE INTO derived(planet,lod,x,y,z,fingerprint,data) VALUES($p,$l,$x,$y,$z,$f,$d);"
                    + "DELETE FROM derived WHERE rowid NOT IN(SELECT rowid FROM derived ORDER BY rowid DESC LIMIT 8192);";
                command.Parameters.AddWithValue("$p", unchecked((long)pending.Key.Planet.Value));
                command.Parameters.AddWithValue("$l", pending.Key.Lod);
                command.Parameters.AddWithValue("$x", pending.Key.X);
                command.Parameters.AddWithValue("$y", pending.Key.Y);
                command.Parameters.AddWithValue("$z", pending.Key.Z);
                command.Parameters.AddWithValue("$f", pending.Entry.Fingerprint);
                command.Parameters.AddWithValue("$d", pending.Entry.Blob);
                command.ExecuteNonQuery();
            }
        }
        catch (SqliteException exception)
        {
            // Rebuildable cache failure: immutable in-memory entries remain authoritative for this session.
            lock (_lock)
            {
                _diskAvailable = false;
            }
            DerivedStoreLog.DiskUnavailable(_logger, exception);
        }
    }

    private sealed record Entry(string Fingerprint, byte[] Blob);
    private readonly record struct Pending(TerrainBlockKey Key, Entry Entry);
}

internal static partial class DerivedStoreLog
{
    [LoggerMessage(EventId = 9701, Level = LogLevel.Warning, Message = "Derived terrain disk cache unavailable; using memory")]
    internal static partial void DiskUnavailable(ILogger logger, Exception exception);
    [LoggerMessage(EventId = 9702, Level = LogLevel.Error, Message = "Derived terrain writer drain exceeded five seconds; database left with active writer")]
    internal static partial void DrainTimedOut(ILogger logger, Exception exception);
}
