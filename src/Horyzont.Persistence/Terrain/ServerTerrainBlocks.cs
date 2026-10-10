using Horyzont.Core.Checkpoints;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Writing;

using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// The server's terrain blocks for its voxel stream: the newest (version, blob) of every block edited since the last committed
/// checkpoint, in front of the saved rows of <see cref="TerrainBlockStore"/>. The terrain adapter records a block right after the
/// edit that bumped its version, so a pending block is never lost by unloading, and a checkpoint takes its payload from here.
/// Thread-safe: <see cref="TryLoad"/> and <see cref="FirstCorruptKey"/> may be called from any thread; every other member is called
/// from the server thread.
/// </summary>
public sealed class ServerTerrainBlocks : ITerrainBlockSource
{
    private const int MaxLoadAttempts = 3;

    private readonly TerrainBlockStore _store;
    private readonly TerrainBufferOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ServerTerrainBlocks> _logger;

    // Guarded by _lock.
    private readonly Lock _lock = new();
    private readonly Dictionary<TerrainBlockKey, Pending> _pending = [];
    private readonly Dictionary<TerrainBlockKey, uint> _known = [];
    private TerrainBlockKey? _firstCorruptKey;
    private int _activeReads;
    private bool _closed;
    private bool _saturated;
    private TaskCompletionSource? _drained;

    /// <summary>Creates the buffer over the saved blocks of a loaded world.</summary>
    /// <param name="store">Saved blocks of the world file.</param>
    /// <param name="savedVersions">
    /// <c>WorldState.TerrainVersions.All</c> right after loading: every block ever edited. Other blocks are served by the generator
    /// without a database query.
    /// </param>
    /// <param name="options">Limits.</param>
    /// <param name="time">Clock of <see cref="CloseAsync"/>.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="ArgumentException">A saved version is 0 or a key repeats.</exception>
    public ServerTerrainBlocks(
        TerrainBlockStore store, IEnumerable<TerrainBlockVersion> savedVersions, TerrainBufferOptions options, TimeProvider time,
        ILogger<ServerTerrainBlocks> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(savedVersions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.EarlyCheckpointBlocks, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxPendingBlocks, options.EarlyCheckpointBlocks, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CloseTimeout, TimeSpan.Zero, nameof(options));
        _store = store;
        _options = options;
        _time = time;
        _logger = logger;
        foreach (var saved in savedVersions)
        {
            if (saved.Version == 0 || !_known.TryAdd(saved.Key, saved.Version))
            {
                throw new ArgumentException($"Saved terrain version {saved.Key} {saved.Version} is 0 or repeated.", nameof(savedVersions));
            }
        }
    }

    /// <summary>Number of blocks waiting for a committed checkpoint.</summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>True from <see cref="TerrainBufferOptions.EarlyCheckpointBlocks"/> pending blocks: checkpoint now if none is in flight.</summary>
    public bool NeedsEarlyCheckpoint => PendingCount >= _options.EarlyCheckpointBlocks;

    /// <summary>True from <see cref="TerrainBufferOptions.MaxPendingBlocks"/> pending blocks: refuse new terrain edits.</summary>
    public bool IsSaturated => PendingCount >= _options.MaxPendingBlocks;

    /// <summary>The first block found with a version but without saved data; null while none was found.</summary>
    public TerrainBlockKey? FirstCorruptKey
    {
        get
        {
            lock (_lock)
            {
                return _firstCorruptKey;
            }
        }
    }

    /// <summary>Records the data of a block right after the edit that gave it <paramref name="version"/> (server thread).</summary>
    /// <param name="key">Block key.</param>
    /// <param name="version">The new version.</param>
    /// <param name="blob">The block blob; from now on nobody may modify the array.</param>
    /// <exception cref="InvalidOperationException">The version is not above the newest known version of the block.</exception>
    public void Record(TerrainBlockKey key, uint version, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        lock (_lock)
        {
            if (_known.TryGetValue(key, out var known) ? version <= known : version == 0)
            {
                throw new InvalidOperationException($"Terrain block {key} recorded with version {version}, not above the known version {known}.");
            }

            _known[key] = version;
            _pending[key] = new Pending(version, blob);
            if (!_saturated && _pending.Count >= _options.MaxPendingBlocks)
            {
                _saturated = true;
                TerrainLog.Saturated(_logger, _pending.Count);
            }
        }
    }

    /// <summary>
    /// Reads a block for the voxel stream (any thread): the pending entry, else the saved row; a block that was never edited is
    /// <see cref="TerrainBlockLookup.NotEdited"/> without a database query. A block with a version is never left to the generator.
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
            uint known;
            lock (_lock)
            {
                if (_closed)
                {
                    return TerrainBlockLookup.Unavailable;
                }

                if (_pending.TryGetValue(key, out var pending))
                {
                    version = pending.Version;
                    blob = pending.Blob;
                    return TerrainBlockLookup.Found;
                }

                if (!_known.TryGetValue(key, out known))
                {
                    return TerrainBlockLookup.NotEdited;
                }

                _activeReads++;
            }

            var retry = false;
            try
            {
                var found = _store.TryLoad(key, out var savedVersion, out var data);
                lock (_lock)
                {
                    // A Record during the query may have put a newer pair in the buffer, or even committed it and dropped the entry.
                    if (_pending.TryGetValue(key, out var pending))
                    {
                        version = pending.Version;
                        blob = pending.Blob;
                        return TerrainBlockLookup.Found;
                    }

                    var latest = _known[key];
                    if (latest != known)
                    {
                        retry = true;
                    }
                    else if (found && savedVersion == latest)
                    {
                        version = savedVersion;
                        blob = data;
                        return TerrainBlockLookup.Found;
                    }
                    else
                    {
                        _firstCorruptKey ??= key;
                        TerrainLog.BlockWithoutData(_logger, key, known);
                        return TerrainBlockLookup.Unavailable;
                    }
                }
            }
            catch (Exception exception)
            {
                // Not swallowed: logged, and the stream reports a load error for the block instead of falling back to the generator.
                TerrainLog.ReadFailed(_logger, exception, key);
                version = 0;
                blob = [];
                return TerrainBlockLookup.Unavailable;
            }
            finally
            {
                lock (_lock)
                {
                    _activeReads--;
                    if (_activeReads == 0 && _closed)
                    {
                        _drained?.TrySetResult();
                    }
                }
            }

            if (!retry)
            {
                break;
            }
        }

        TerrainLog.LoadRetriesExhausted(_logger, key, MaxLoadAttempts);
        return TerrainBlockLookup.Unavailable;
    }

    /// <summary>
    /// Builds the checkpoint request of a captured checkpoint (server thread, right after <c>CaptureCheckpoint</c>): exactly one
    /// payload per captured version, with the pending blob of that version.
    /// </summary>
    /// <param name="core">The captured checkpoint.</param>
    /// <returns>The request for <see cref="PersistenceWriter.CheckpointAsync"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// A captured version has no pending entry of the same version (a bug; the caller requeues the checkpoint).
    /// </exception>
    public CheckpointRequest CreateCheckpointRequest(WorldCheckpoint core)
    {
        ArgumentNullException.ThrowIfNull(core);
        var payloads = new TerrainBlockPayload[core.TerrainVersions.Count];
        lock (_lock)
        {
            for (var i = 0; i < payloads.Length; i++)
            {
                var captured = core.TerrainVersions[i];
                if (!_pending.TryGetValue(captured.Key, out var pending) || pending.Version != captured.Version)
                {
                    throw new InvalidOperationException(
                        $"Terrain block {captured.Key} version {captured.Version} has no pending data of that version.");
                }

                payloads[i] = new TerrainBlockPayload(captured.Key, captured.Version, pending.Blob);
            }
        }

        return new CheckpointRequest(core, payloads);
    }

    /// <summary>
    /// After <see cref="CheckpointCommitted"/> (server thread): removes the pending entries saved by the checkpoint. An entry with a
    /// newer version (an edit after the barrier) stays.
    /// </summary>
    /// <param name="request">The committed request.</param>
    public void OnCheckpointCommitted(CheckpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_lock)
        {
            foreach (var payload in request.TerrainBlocks)
            {
                if (_pending.TryGetValue(payload.Key, out var pending) && pending.Version == payload.Version)
                {
                    _pending.Remove(payload.Key);
                }
            }

            if (_saturated && _pending.Count < _options.MaxPendingBlocks)
            {
                _saturated = false;
                TerrainLog.SaturationCleared(_logger, _pending.Count);
            }
        }
    }

    /// <summary>
    /// After <see cref="CheckpointFailed"/> (server thread): keeps every entry; <c>WorldState.RequeueAfterFailedCheckpoint</c> marks the
    /// blocks again, so the next checkpoint takes them.
    /// </summary>
    /// <param name="request">The failed request.</param>
    public void OnCheckpointFailed(CheckpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        TerrainLog.CheckpointFailedKept(_logger, request.Core.BarrierOpId.Value, request.TerrainBlocks.Count);
    }

    /// <summary>
    /// Stops serving reads: from now on <see cref="TryLoad"/> returns <see cref="TerrainBlockLookup.Unavailable"/> without touching
    /// SQLite. Waits at most <see cref="TerrainBufferOptions.CloseTimeout"/> for reads in progress; only after <c>true</c> may the
    /// caller close the <see cref="TerrainBlockStore"/>.
    /// </summary>
    /// <returns>True when no read is running any more; false when the deadline passed first.</returns>
    public async Task<bool> CloseAsync()
    {
        Task drained;
        lock (_lock)
        {
            _closed = true;
            if (_activeReads == 0)
            {
                return true;
            }

            _drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drained = _drained.Task;
        }

        try
        {
            await drained.WaitAsync(_options.CloseTimeout, _time).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            int reads;
            lock (_lock)
            {
                reads = _activeReads;
            }

            TerrainLog.CloseTimedOut(_logger, reads, (int)_options.CloseTimeout.TotalMilliseconds);
            return false;
        }
    }

    private readonly record struct Pending(uint Version, byte[] Blob);
}
