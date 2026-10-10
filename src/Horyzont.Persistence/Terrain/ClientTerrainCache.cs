using System.Threading.Channels;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// The remote client's cache of verified, authoritative terrain blocks, and the block source of its own voxel stream. Blocks live in
/// an immutable memory overlay (visible at once) and are written behind by one writer to a SQLite file of their own; the server's
/// world is never touched. A disk error or corruption is logged and the cache carries on in memory only.
/// Thread-safe: every member may be called from any thread.
/// </summary>
public sealed partial class ClientTerrainCache : ITerrainBlockSource
{
    private const int MaxLoadAttempts = 3;

    private readonly string _path;
    private readonly string _fingerprint;
    private readonly TimeProvider _time;
    private readonly ILogger<ClientTerrainCache> _logger;
    private readonly string _readConnectionString;
    private readonly Channel<bool> _signal = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writerTask;

    // Guarded by _lock.
    private readonly Lock _lock = new();
    private readonly SortedDictionary<TerrainBlockKey, Entry> _index = [];
    private readonly Dictionary<TerrainBlockKey, (uint Version, ulong Hash)> _expected = [];
    private readonly Dictionary<TerrainBlockKey, int> _pins = [];
    private readonly Dictionary<TerrainBlockKey, WriteOp> _queue = [];
    private long _clock;
    private long _memoryBytes;
    private long _diskBytes;
    private int _queuedPuts;
    private int _inflightPuts;
    private int _activeReads;
    private bool _closed;
    private bool _diskFailed;
    private bool _loggedQueueFull;
    private bool _batchRunning;
    private TaskCompletionSource? _readsDrained;
    private TaskCompletionSource? _idle;

    /// <summary>Opens (creating when needed) the cache file; a damaged or unwritable file leaves a memory-only cache.</summary>
    /// <param name="databasePath">The cache file, <c>cache/&lt;WorldId&gt;/terrain.db</c>.</param>
    /// <param name="metadataFingerprint">
    /// World id, build hash, profile, planet parameters and semantics version in one string; cached blocks of any other fingerprint
    /// are dropped.
    /// </param>
    /// <param name="time">Clock of <see cref="CloseAsync"/> and of the usage stamps.</param>
    /// <param name="logger">Logger.</param>
    public ClientTerrainCache(string databasePath, string metadataFingerprint, TimeProvider time, ILogger<ClientTerrainCache> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentException.ThrowIfNullOrEmpty(metadataFingerprint);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _path = Path.GetFullPath(databasePath);
        _fingerprint = metadataFingerprint;
        _time = time;
        _logger = logger;
        _readConnectionString = WorldDatabase.ConnectionString(_path, SqliteOpenMode.ReadOnly, pooling: false);
        var connection = OpenDatabase();
        _diskFailed = connection is null;
        _writerTask = connection is null ? Task.CompletedTask : Task.Run(() => WriterLoopAsync(connection));
    }

    /// <summary>Largest total of blob bytes kept in the file; the least recently used unpinned blocks go first.</summary>
    internal long MaxDiskBytes { get; set; } = ClientTerrainCacheSchema.DefaultMaxDiskBytes;

    /// <summary>Largest total of blob bytes held in memory; blocks already on disk drop their blob first.</summary>
    internal long MaxMemoryBytes { get; set; } = ClientTerrainCacheSchema.DefaultMaxMemoryBytes;

    /// <summary>How long <see cref="CloseAsync"/> waits for active reads and the writer.</summary>
    internal TimeSpan CloseTimeout { get; set; } = ClientTerrainCacheSchema.DefaultCloseTimeout;

    /// <summary>Test hook: called on the writer thread before a batch is written.</summary>
    internal Action? BeforeBatch { get; set; }

    /// <summary>Test hook: called on the reading thread before a disk read.</summary>
    internal Action<TerrainBlockKey>? BeforeRead { get; set; }

    /// <summary>True once the file could not be opened or written: the cache lives in memory only.</summary>
    public bool IsMemoryOnly
    {
        get
        {
            lock (_lock)
            {
                return _diskFailed;
            }
        }
    }

    /// <summary>Number of cached blocks.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _index.Count;
            }
        }
    }

    /// <summary>Blob bytes held in the memory overlay.</summary>
    public long MemoryBytes
    {
        get
        {
            lock (_lock)
            {
                return _memoryBytes;
            }
        }
    }

    /// <summary>Blob bytes written to the file.</summary>
    public long DiskBytes
    {
        get
        {
            lock (_lock)
            {
                return _diskBytes;
            }
        }
    }

    /// <summary>
    /// Reads a block for the voxel stream (any thread). A block with data is <see cref="TerrainBlockLookup.Found"/> (version 0 too: a
    /// generator fix); a key the server said changed but whose matching data is not here is <see cref="TerrainBlockLookup.Unavailable"/>
    /// (see <see cref="Expect"/>); an unknown key may be generated for now.
    /// </summary>
    /// <param name="key">Block key.</param>
    /// <param name="version">The version when found.</param>
    /// <param name="blob">The shared, immutable blob when found; otherwise empty.</param>
    /// <returns>The lookup result.</returns>
    public TerrainBlockLookup TryLoad(TerrainBlockKey key, out uint version, out byte[] blob)
    {
        version = 0;
        blob = [];
        for (var attempt = 0; attempt < MaxLoadAttempts; attempt++)
        {
            Entry entry;
            lock (_lock)
            {
                if (_closed)
                {
                    return TerrainBlockLookup.Unavailable;
                }

                var expecting = _expected.TryGetValue(key, out var expected);
                if (!_index.TryGetValue(key, out entry!))
                {
                    return expecting && expected.Version != 0 ? TerrainBlockLookup.Unavailable : TerrainBlockLookup.NotEdited;
                }

                if (expecting && (entry.Version != expected.Version || entry.Hash != expected.Hash))
                {
                    return expected.Version != 0 ? TerrainBlockLookup.Unavailable : TerrainBlockLookup.NotEdited;
                }

                entry.Stamp = ++_clock;
                if (entry.Blob is { } held)
                {
                    version = entry.Version;
                    blob = held;
                    return TerrainBlockLookup.Found;
                }

                if (_diskFailed)
                {
                    return TerrainBlockLookup.Unavailable;
                }

                _activeReads++;
            }

            var outcome = ReadFromDisk(key, entry, out version, out blob);
            if (outcome is not ReadOutcome.Retry)
            {
                return outcome switch
                {
                    ReadOutcome.Found => TerrainBlockLookup.Found,
                    ReadOutcome.Missing => LookupWithoutData(key),
                    _ => TerrainBlockLookup.Unavailable,
                };
            }
        }

        Log.LoadRetriesExhausted(_logger, key, MaxLoadAttempts);
        return TerrainBlockLookup.Unavailable;
    }

    /// <summary>Hash of the held block (the <c>TerrainSyncEntry.Hash</c> it was stored with).</summary>
    /// <param name="key">Block key.</param>
    /// <param name="hash">The hash when held; otherwise 0.</param>
    /// <returns>Whether the block is held.</returns>
    public bool TryGetHash(TerrainBlockKey key, out ulong hash)
    {
        lock (_lock)
        {
            if (_index.TryGetValue(key, out var entry))
            {
                hash = entry.Hash;
                return true;
            }
        }

        hash = 0;
        return false;
    }

    /// <summary>
    /// Stores authoritative server data, replacing whatever is held for the key: also an older version or another hash (the server
    /// state wins after a lost partial save). Never call it with a block shaped by local prediction. Version 0 with data is a generator
    /// fix. The blob is copied.
    /// </summary>
    /// <param name="key">Block key.</param>
    /// <param name="version">Authoritative version.</param>
    /// <param name="hash">Authoritative hash of the block.</param>
    /// <param name="blob">The block blob (<c>StateCodec.EncodeTerrainBlockBlob</c>); the caller keeps its array.</param>
    public void Put(TerrainBlockKey key, uint version, ulong hash, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        var copy = (byte[])blob.Clone();
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            var entry = new Entry(version, hash, copy) { Stamp = ++_clock };
            RemoveFromAccountingLocked(key);
            _index[key] = entry;
            _memoryBytes += copy.Length;
            if (_expected.TryGetValue(key, out var expected) && expected.Version == version && expected.Hash == hash)
            {
                _expected.Remove(key);
            }

            QueuePutLocked(key, entry);
            EvictMemoryLocked();
        }
    }

    /// <summary>Drops the held block (memory and file), e.g. after a corrupt blob; an expectation for the key stays.</summary>
    /// <param name="key">Block key.</param>
    public void Invalidate(TerrainBlockKey key)
    {
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            RemoveEntryLocked(key, queueDelete: true);
        }
    }

    /// <summary>
    /// Records the server's answer for a key: the version and hash that are authoritative now. Held data that matches stays
    /// <see cref="TerrainBlockLookup.Found"/>; otherwise the key is <see cref="TerrainBlockLookup.Unavailable"/> until a matching
    /// <see cref="Put"/> arrives (version 0: the held data is dropped and the generator serves the block).
    /// </summary>
    /// <param name="key">Block key.</param>
    /// <param name="version">Authoritative version.</param>
    /// <param name="hash">Authoritative hash.</param>
    public void Expect(TerrainBlockKey key, uint version, ulong hash)
    {
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            var matches = _index.TryGetValue(key, out var entry) && entry.Version == version && entry.Hash == hash;
            if (version == 0)
            {
                _expected.Remove(key);
                if (entry is not null && !matches)
                {
                    RemoveEntryLocked(key, queueDelete: true);
                }
            }
            else if (matches)
            {
                _expected.Remove(key);
            }
            else
            {
                _expected[key] = (version, hash);
            }
        }
    }

    /// <summary>Keeps the block out of eviction while a prediction depends on it; calls nest.</summary>
    /// <param name="key">Block key.</param>
    public void Pin(TerrainBlockKey key)
    {
        lock (_lock)
        {
            _pins[key] = _pins.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>Releases one <see cref="Pin"/>.</summary>
    /// <param name="key">Block key.</param>
    public void Unpin(TerrainBlockKey key)
    {
        lock (_lock)
        {
            if (_pins.TryGetValue(key, out var count))
            {
                if (count <= 1)
                {
                    _pins.Remove(key);
                }
                else
                {
                    _pins[key] = count - 1;
                }
            }
        }
    }

    /// <summary>Copies the held (key, version) pairs of a planet, in key order, for the cache report.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="destination">Receives the pairs.</param>
    /// <param name="offset">Number of the planet's pairs to skip, so that a long report is read in pages.</param>
    /// <returns>Number of pairs written; less than <c>destination.Length</c> at the end.</returns>
    public int CopyVersions(PlanetId planet, Span<TerrainBlockVersion> destination, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var written = 0;
        var skipped = 0;
        lock (_lock)
        {
            foreach (var pair in _index)
            {
                if (pair.Key.Planet != planet)
                {
                    continue;
                }

                if (skipped < offset)
                {
                    skipped++;
                    continue;
                }

                if (written == destination.Length)
                {
                    break;
                }

                destination[written++] = new TerrainBlockVersion(pair.Key, pair.Value.Version);
            }
        }

        return written;
    }

    /// <summary>
    /// Stops serving reads and writes, waits at most five seconds (<see cref="TimeProvider"/>) for reads in progress and for the
    /// writer to save the queue. A read or the writer that outlives the deadline keeps its own connection until it finishes, so the
    /// database is never closed under it.
    /// </summary>
    /// <returns>True when nothing is running any more; false when the deadline passed first.</returns>
    public async Task<bool> CloseAsync()
    {
        Task reads;
        lock (_lock)
        {
            _closed = true;
            if (_activeReads == 0)
            {
                reads = Task.CompletedTask;
            }
            else
            {
                _readsDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                reads = _readsDrained.Task;
            }
        }

        _signal.Writer.TryComplete();
        try
        {
            await Task.WhenAll(reads, _writerTask).WaitAsync(CloseTimeout, _time).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            int activeReads;
            int queued;
            lock (_lock)
            {
                activeReads = _activeReads;
                queued = _queuedPuts + _inflightPuts;
            }

            Log.CloseTimedOut(_logger, activeReads, queued, (int)CloseTimeout.TotalMilliseconds);
            return false;
        }
    }

    /// <summary>Test helper: completes when the write queue has been saved (or discarded after a disk failure).</summary>
    internal Task FlushAsync()
    {
        lock (_lock)
        {
            if (_queue.Count == 0 && !_batchRunning)
            {
                return Task.CompletedTask;
            }

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _idle.Task;
        }
    }

    private TerrainBlockLookup LookupWithoutData(TerrainBlockKey key)
    {
        lock (_lock)
        {
            return _expected.TryGetValue(key, out var expected) && expected.Version != 0
                ? TerrainBlockLookup.Unavailable
                : TerrainBlockLookup.NotEdited;
        }
    }

    private ReadOutcome ReadFromDisk(TerrainBlockKey key, Entry entry, out uint version, out byte[] blob)
    {
        version = 0;
        blob = [];
        uint rowVersion = 0;
        ulong rowHash = 0;
        byte[]? data = null;
        Exception? failure = null;
        try
        {
            BeforeRead?.Invoke(key);
            using var connection = WorldDatabase.OpenConnection(_readConnectionString, readOnly: true);
            using var command = Sql.Command(connection, null, ClientTerrainCacheSchema.SelectBlockSql);
            AddKey(command, key);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                rowVersion = checked((uint)reader.GetInt64(0));
                rowHash = Sql.FromDb(reader.GetInt64(1));
                data = (byte[])reader.GetValue(2);
            }
        }
        catch (Exception e) when (e is SqliteException or IOException or OverflowException or InvalidCastException)
        {
            // Not swallowed: logged below by the disk failure handling; the stream reports a load error for the block.
            failure = e;
        }

        var outcome = ReadOutcome.Failed;
        lock (_lock)
        {
            _activeReads--;
            if (_activeReads == 0 && _closed)
            {
                _readsDrained?.TrySetResult();
            }

            if (failure is null)
            {
                var current = _index.TryGetValue(key, out var held) && ReferenceEquals(held, entry);
                if (data is not null && rowVersion == entry.Version && rowHash == entry.Hash)
                {
                    if (current && entry.Blob is null)
                    {
                        entry.Blob = data;
                        _memoryBytes += data.Length;
                        EvictMemoryLocked();
                    }

                    version = entry.Version;
                    blob = data;
                    outcome = ReadOutcome.Found;
                }
                else if (!current)
                {
                    outcome = ReadOutcome.Retry;
                }
                else
                {
                    // The row is gone or differs: the index no longer describes the file, so forget the block.
                    RemoveEntryLocked(key, queueDelete: false);
                    outcome = ReadOutcome.Missing;
                }
            }
        }

        if (failure is not null)
        {
            HandleDiskFailure(failure);
        }

        return outcome;
    }

    private SqliteConnection? OpenDatabase()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return OpenAndPrepare();
            }
            catch (SqliteException e) when (attempt == 0 && WorldDatabase.IsCorruption(e))
            {
                Log.Recreated(_logger, e, _path);
                try
                {
                    Sql.DeleteWithSideFiles(_path);
                }
                catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException)
                {
                    // Safe: the cache simply stays in memory; the next start tries the file again.
                    Log.DiskFailed(_logger, deleteFailure, _path);
                    return null;
                }
            }
            catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
            {
                // Safe: the cache is only an optimization; without the file everything is fetched from the server again.
                Log.DiskFailed(_logger, e, _path);
                return null;
            }
        }
    }

    private SqliteConnection OpenAndPrepare()
    {
        var connection = WorldDatabase.OpenConnection(WorldDatabase.ConnectionString(_path, SqliteOpenMode.ReadWriteCreate, pooling: false), readOnly: false);
        try
        {
            WorldDatabase.Execute(connection, "PRAGMA synchronous = NORMAL;");
            WorldDatabase.Execute(connection, ClientTerrainCacheSchema.CreateSql);
            var schema = ReadMeta(connection, ClientTerrainCacheSchema.MetaSchema);
            var fingerprint = ReadMeta(connection, ClientTerrainCacheSchema.MetaFingerprint);
            if (schema != ClientTerrainCacheSchema.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) || fingerprint != _fingerprint)
            {
                using var transaction = connection.BeginTransaction();
                WorldDatabase.Execute(connection, ClientTerrainCacheSchema.DeleteAllBlocksSql, transaction);
                WriteMeta(connection, transaction, ClientTerrainCacheSchema.MetaSchema, ClientTerrainCacheSchema.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                WriteMeta(connection, transaction, ClientTerrainCacheSchema.MetaFingerprint, _fingerprint);
                transaction.Commit();
                if (schema is not null || fingerprint is not null)
                {
                    Log.MetadataMismatch(_logger, _path);
                }
            }

            LoadIndex(connection);
            Log.Opened(_logger, _path, _index.Count);
            return connection;
        }
        catch
        {
            connection.Dispose();
            _index.Clear();
            _memoryBytes = 0;
            _diskBytes = 0;
            throw;
        }
    }

    private void LoadIndex(SqliteConnection connection)
    {
        using var command = Sql.Command(connection, null, ClientTerrainCacheSchema.SelectIndexSql);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = new TerrainBlockKey(
                new PlanetId(Sql.FromDb(reader.GetInt64(0))), checked((byte)reader.GetInt64(1)),
                checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(3)), checked((int)reader.GetInt64(4)));
            var size = checked((int)reader.GetInt64(7));
            _index[key] = new Entry(checked((uint)reader.GetInt64(5)), Sql.FromDb(reader.GetInt64(6)), null)
            {
                Size = size,
                Persisted = true,
                Stamp = ++_clock,
            };
            _diskBytes += size;
        }
    }

    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using var command = Sql.Command(connection, null, ClientTerrainCacheSchema.SelectMetaSql);
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void WriteMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = Sql.Command(connection, transaction, ClientTerrainCacheSchema.UpsertMetaSql);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void AddKey(SqliteCommand command, TerrainBlockKey key)
    {
        command.Parameters.AddWithValue("$p", Sql.ToDb(key.Planet.Value));
        command.Parameters.AddWithValue("$lod", (long)key.Lod);
        command.Parameters.AddWithValue("$x", (long)key.X);
        command.Parameters.AddWithValue("$y", (long)key.Y);
        command.Parameters.AddWithValue("$z", (long)key.Z);
    }

    // Accounting of an entry that is about to be replaced or removed (under _lock).
    private void RemoveFromAccountingLocked(TerrainBlockKey key)
    {
        if (_index.TryGetValue(key, out var old))
        {
            _memoryBytes -= old.Blob?.Length ?? 0;
            if (old.Persisted)
            {
                _diskBytes -= old.Size;
            }
        }
    }

    private void RemoveEntryLocked(TerrainBlockKey key, bool queueDelete)
    {
        if (!_index.TryGetValue(key, out var entry))
        {
            return;
        }

        RemoveFromAccountingLocked(key);
        _index.Remove(key);
        if (queueDelete && (entry.Persisted || entry.Queued))
        {
            QueueDeleteLocked(key);
        }
    }

    private void QueuePutLocked(TerrainBlockKey key, Entry entry)
    {
        if (_diskFailed)
        {
            return;
        }

        DropQueuedPutLocked(key);
        if (_queuedPuts + _inflightPuts >= ClientTerrainCacheSchema.MaxQueuedWrites)
        {
            // The queue is full: this block stays in memory only, and a row it would have replaced must not survive as stale data.
            if (!_loggedQueueFull)
            {
                _loggedQueueFull = true;
                Log.WriteQueueFull(_logger, ClientTerrainCacheSchema.MaxQueuedWrites);
            }

            QueueDeleteLocked(key);
            return;
        }

        _queue[key] = new WriteOp(key, entry);
        entry.Queued = true;
        _queuedPuts++;
        _signal.Writer.TryWrite(true);
    }

    private void QueueDeleteLocked(TerrainBlockKey key)
    {
        if (_diskFailed)
        {
            return;
        }

        DropQueuedPutLocked(key);
        _queue[key] = new WriteOp(key, null);
        _signal.Writer.TryWrite(true);
    }

    private void DropQueuedPutLocked(TerrainBlockKey key)
    {
        if (_queue.Remove(key, out var queued) && queued.Entry is { } entry)
        {
            entry.Queued = false;
            _queuedPuts--;
        }
    }

    // Brings memory under the limit (under _lock): blocks already on disk lose their blob, others (memory-only mode, or not saved
    // because the queue was full) are forgotten. Pinned and still-queued blocks stay; least recently used go first.
    private void EvictMemoryLocked()
    {
        if (_memoryBytes <= MaxMemoryBytes)
        {
            return;
        }

        var target = MaxMemoryBytes - (MaxMemoryBytes / 10);
        var candidates = new List<KeyValuePair<TerrainBlockKey, Entry>>();
        foreach (var pair in _index)
        {
            if (pair.Value.Blob is not null && !pair.Value.Queued && !_pins.ContainsKey(pair.Key))
            {
                candidates.Add(pair);
            }
        }

        candidates.Sort(static (a, b) => a.Value.Stamp.CompareTo(b.Value.Stamp));
        foreach (var (key, entry) in candidates)
        {
            if (_memoryBytes <= target)
            {
                break;
            }

            if (entry.Persisted)
            {
                _memoryBytes -= entry.Blob!.Length;
                entry.Blob = null;
            }
            else
            {
                RemoveEntryLocked(key, queueDelete: false);
            }
        }
    }

    // Brings the file under the limit (under _lock): least recently used unpinned blocks are deleted.
    private void EvictDiskLocked()
    {
        if (_diskBytes <= MaxDiskBytes)
        {
            return;
        }

        var candidates = new List<KeyValuePair<TerrainBlockKey, Entry>>();
        foreach (var pair in _index)
        {
            if (pair.Value.Persisted && !_pins.ContainsKey(pair.Key))
            {
                candidates.Add(pair);
            }
        }

        candidates.Sort(static (a, b) => a.Value.Stamp.CompareTo(b.Value.Stamp));
        foreach (var (key, _) in candidates)
        {
            if (_diskBytes <= MaxDiskBytes)
            {
                break;
            }

            RemoveEntryLocked(key, queueDelete: true);
        }
    }

    private async Task WriterLoopAsync(SqliteConnection connection)
    {
        try
        {
            while (await _signal.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_signal.Reader.TryRead(out _))
                {
                }

                WriteBatch(connection);
            }

            while (WriteBatch(connection))
            {
            }
        }
        finally
        {
            connection.Dispose();
        }
    }

    private bool WriteBatch(SqliteConnection connection)
    {
        WriteOp[] ops;
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                CompleteIdleLocked();
                return false;
            }

            _batchRunning = true;
            ops = [.. _queue.Values];
            _queue.Clear();
            _inflightPuts = _queuedPuts;
            _queuedPuts = 0;
        }

        var used = _time.GetUtcNow().UtcTicks;
        try
        {
            BeforeBatch?.Invoke();
            lock (_lock)
            {
                if (_diskFailed)
                {
                    ReleaseBatchLocked(ops);
                    return true;
                }
            }

            using var transaction = connection.BeginTransaction();
            foreach (var op in ops)
            {
                using var command = Sql.Command(connection, transaction, op.Entry is null ? ClientTerrainCacheSchema.DeleteBlockSql : ClientTerrainCacheSchema.UpsertBlockSql);
                AddKey(command, op.Key);
                if (op.Entry is { } entry)
                {
                    command.Parameters.AddWithValue("$version", (long)entry.Version);
                    command.Parameters.AddWithValue("$hash", Sql.ToDb(entry.Hash));
                    command.Parameters.AddWithValue("$used", used);
                    command.Parameters.AddWithValue("$data", entry.Blob!);
                }

                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception e) when (e is SqliteException or IOException)
        {
            // Not swallowed: logged by the disk failure handling, which switches the cache to memory only.
            HandleDiskFailure(e);
            lock (_lock)
            {
                ReleaseBatchLocked(ops);
            }

            return true;
        }

        lock (_lock)
        {
            foreach (var op in ops)
            {
                if (op.Entry is not { } entry)
                {
                    continue;
                }

                entry.Queued = false;
                if (_index.TryGetValue(op.Key, out var current) && ReferenceEquals(current, entry))
                {
                    entry.Persisted = true;
                    _diskBytes += entry.Size;
                }
            }

            _inflightPuts = 0;
            _batchRunning = false;
            EvictMemoryLocked();
            EvictDiskLocked();
            CompleteIdleLocked();
        }

        return true;
    }

    private void ReleaseBatchLocked(WriteOp[] ops)
    {
        foreach (var op in ops)
        {
            if (op.Entry is { } entry)
            {
                entry.Queued = false;
            }
        }

        _inflightPuts = 0;
        _batchRunning = false;
        CompleteIdleLocked();
    }

    private void CompleteIdleLocked()
    {
        if (_queue.Count == 0 && !_batchRunning)
        {
            _idle?.TrySetResult();
            _idle = null;
        }
    }

    // Switches to memory only after a disk error or corruption: the queue is dropped and blocks that exist only on disk are
    // forgotten (the next manifest fetches them again). Only the client cache is affected, never the server's world.
    private void HandleDiskFailure(Exception exception)
    {
        lock (_lock)
        {
            if (_diskFailed)
            {
                return;
            }

            _diskFailed = true;
            _queue.Clear();
            _queuedPuts = 0;
            _diskBytes = 0;
            List<TerrainBlockKey>? disk = null;
            foreach (var (key, entry) in _index)
            {
                entry.Queued = false;
                entry.Persisted = false;
                if (entry.Blob is null)
                {
                    (disk ??= []).Add(key);
                }
            }

            if (disk is not null)
            {
                foreach (var key in disk)
                {
                    _index.Remove(key);
                }
            }

        }

        Log.DiskFailed(_logger, exception, _path);
    }

    private enum ReadOutcome
    {
        Found,
        Missing,
        Failed,
        Retry,
    }

    private readonly record struct WriteOp(TerrainBlockKey Key, Entry? Entry);

    private sealed class Entry(uint version, ulong hash, byte[]? blob)
    {
        internal uint Version { get; } = version;

        internal ulong Hash { get; } = hash;

        // Null while only the file has the data (loaded at start, or dropped by the memory limit).
        internal byte[]? Blob { get; set; } = blob;

        internal int Size { get; init; } = blob?.Length ?? 0;

        internal bool Persisted { get; set; }

        internal bool Queued { get; set; }

        internal long Stamp { get; set; }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 140, Level = LogLevel.Information, Message = "Client terrain cache {Path} opened with {Count} blocks")]
        internal static partial void Opened(ILogger logger, string path, int count);

        [LoggerMessage(EventId = 141, Level = LogLevel.Warning, Message = "Client terrain cache {Path} belongs to another world, build or schema; its cached blocks were dropped")]
        internal static partial void MetadataMismatch(ILogger logger, string path);

        [LoggerMessage(EventId = 142, Level = LogLevel.Warning, Message = "Client terrain cache {Path} is damaged; it is deleted and created again")]
        internal static partial void Recreated(ILogger logger, Exception exception, string path);

        [LoggerMessage(EventId = 143, Level = LogLevel.Error, Message = "Client terrain cache {Path} cannot be used (disk error or corruption); continuing in memory only, the server world is not affected")]
        internal static partial void DiskFailed(ILogger logger, Exception exception, string path);

        [LoggerMessage(EventId = 144, Level = LogLevel.Warning, Message = "Client terrain cache write queue is full ({Limit}); further blocks stay in memory only until it drains")]
        internal static partial void WriteQueueFull(ILogger logger, int limit);

        [LoggerMessage(EventId = 146, Level = LogLevel.Error, Message = "Client terrain block {Key} changed during each of {Attempts} read attempts; the block cannot be loaded now")]
        internal static partial void LoadRetriesExhausted(ILogger logger, TerrainBlockKey key, int attempts);

        [LoggerMessage(EventId = 145, Level = LogLevel.Warning, Message = "Client terrain cache closed with {Reads} reads and {Queued} writes still running after {TimeoutMs} ms; the database stays open for them")]
        internal static partial void CloseTimedOut(ILogger logger, int reads, int queued, int timeoutMs);
    }
}
