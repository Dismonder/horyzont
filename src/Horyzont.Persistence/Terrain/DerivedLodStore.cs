using System.Threading.Channels;

using Horyzont.Core.World.Terrain;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// Disposable derived terrain database; a bounded writer persists immutable, fingerprinted mipmaps. The newest
/// <see cref="DefaultMaxEntries"/> rows are read once in the constructor (a bounded, rebuildable cache of small blobs, so the start
/// is not moved to a background task); the file is trimmed in batches by the writer, and the memory map evicts the least recently
/// used entries.
/// </summary>
public sealed class DerivedLodStore
{
    /// <summary>Most mipmaps kept in memory and in the file.</summary>
    public const int DefaultMaxEntries = 8192;

    private readonly int _maxEntries;
    private readonly int _trimEvery;
    private long _clock;
    private bool _writerFailed;
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
    /// <param name="maxEntries">Most entries kept in memory and in the file.</param>
    public DerivedLodStore(string databasePath, TimeProvider time, ILogger<DerivedLodStore> logger, int maxEntries = DefaultMaxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 2);
        _maxEntries = maxEntries;
        _trimEvery = Math.Max(1, maxEntries / 8);
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
            command.CommandText = "SELECT planet,lod,x,y,z,fingerprint,data FROM derived ORDER BY rowid DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", _maxEntries);
            using var reader = command.ExecuteReader();
            var loaded = new List<(TerrainBlockKey Key, Entry Entry)>();
            while (reader.Read())
            {
                var key = new TerrainBlockKey(new Core.Ids.PlanetId(unchecked((ulong)reader.GetInt64(0))),
                    checked((byte)reader.GetInt64(1)), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
                loaded.Add((key, new Entry(reader.GetString(5), (byte[])reader.GetValue(6))));
            }
            // Newest row first: the oldest loaded row gets the lowest usage stamp.
            var stamp = -loaded.Count;
            for (var i = loaded.Count - 1; i >= 0; i--)
            {
                loaded[i].Entry.Stamp = stamp++;
                _entries[loaded[i].Key] = loaded[i].Entry;
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

    /// <summary>Gets a value indicating whether the writer stopped because of an unexpected failure (the file may lack recent entries).</summary>
    public bool IsWriterFailed
    {
        get
        {
            lock (_lock)
            {
                return _writerFailed;
            }
        }
    }

    /// <summary>Test hook: called on the writer thread before each entry is written.</summary>
    internal Action? BeforeWrite { get; set; }

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
            if (_entries.Count >= _maxEntries && !_entries.ContainsKey(key))
            {
                EvictLeastRecentlyUsedLocked();
            }
            entry.Stamp = ++_clock;
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
                entry.Stamp = ++_clock;
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
            return !IsWriterFailed;
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

    // Drops the least recently used eighth at once, so the sort is paid once per many inserts (under _lock).
    private void EvictLeastRecentlyUsedLocked()
    {
        var drop = Math.Max(1, _maxEntries / 8);
        var oldest = _entries.OrderBy(static pair => pair.Value.Stamp).Take(drop).Select(static pair => pair.Key).ToList();
        foreach (var key in oldest)
        {
            _entries.Remove(key);
        }
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
            var sinceTrim = 0;
            await foreach (var pending in _writes.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                BeforeWrite?.Invoke();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT OR REPLACE INTO derived(planet,lod,x,y,z,fingerprint,data) VALUES($p,$l,$x,$y,$z,$f,$d);";
                command.Parameters.AddWithValue("$p", unchecked((long)pending.Key.Planet.Value));
                command.Parameters.AddWithValue("$l", pending.Key.Lod);
                command.Parameters.AddWithValue("$x", pending.Key.X);
                command.Parameters.AddWithValue("$y", pending.Key.Y);
                command.Parameters.AddWithValue("$z", pending.Key.Z);
                command.Parameters.AddWithValue("$f", pending.Entry.Fingerprint);
                command.Parameters.AddWithValue("$d", pending.Entry.Blob);
                command.ExecuteNonQuery();
                if (++sinceTrim >= _trimEvery)
                {
                    sinceTrim = 0;
                    Trim(connection);
                }
            }
            Trim(connection);
        }
#pragma warning disable CA1031 // Rebuildable cache: any failure of the writer only costs persistence, so it is logged and the store goes memory-only.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            lock (_lock)
            {
                _diskAvailable = false;
                _writerFailed = true;
            }
            DerivedStoreLog.DiskUnavailable(_logger, exception);
        }
    }

    // Rows are numbered by write order (a replaced row gets a new number), so the oldest writes are far below the newest.
    private void Trim(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM derived WHERE rowid <= (SELECT MAX(rowid) FROM derived) - $keep;";
        command.Parameters.AddWithValue("$keep", _maxEntries);
        command.ExecuteNonQuery();
    }

    private sealed class Entry(string fingerprint, byte[] blob)
    {
        internal string Fingerprint { get; } = fingerprint;
        internal byte[] Blob { get; } = blob;
        internal long Stamp { get; set; }
    }
    private readonly record struct Pending(TerrainBlockKey Key, Entry Entry);
}

internal static partial class DerivedStoreLog
{
    [LoggerMessage(EventId = 9701, Level = LogLevel.Warning, Message = "Derived terrain disk cache unavailable; using memory")]
    internal static partial void DiskUnavailable(ILogger logger, Exception exception);
    [LoggerMessage(EventId = 9702, Level = LogLevel.Error, Message = "Derived terrain writer drain exceeded five seconds; database left with active writer")]
    internal static partial void DrainTimedOut(ILogger logger, Exception exception);
}
