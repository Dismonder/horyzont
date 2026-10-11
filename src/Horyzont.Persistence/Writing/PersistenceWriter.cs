using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Hashing;
using System.Threading.Channels;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Persistence.Backup;
using Horyzont.Persistence.Faults;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence.Writing;

/// <summary>
/// The only writer of a world file: a dedicated thread executes commands in the order they were enqueued. Journal entries are
/// committed in batches (one transaction each) when <see cref="PersistenceOptions.FlushInterval"/> passes after the first entry of
/// the batch, when <see cref="PersistenceOptions.MaxBatch"/> entries wait, before any other command and on dispose. A checkpoint is
/// one transaction that ends by deleting the journal up to its barrier. After a write error the writer is faulted: it writes
/// nothing, keeps journal entries in memory in order and fails checkpoints until <see cref="ClearFault"/>.
/// The writer never touches <c>WorldState</c>; results reach the server loop as <see cref="PersistenceEvent"/>s.
/// </summary>
public sealed class PersistenceWriter : IAsyncDisposable
{
    private readonly WorldDatabase _db;
    private readonly PersistenceOptions _options;
    private readonly TimeProvider _time;
    private readonly IPersistenceFaults _faults;
    private readonly ILogger<PersistenceWriter> _logger;
    private readonly BackupService _backups;
    private readonly Channel<WriteCommand> _channel;
    private readonly ConcurrentQueue<PersistenceEvent> _events = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private readonly ITimer? _backupTimer;
    private readonly string _backupSource;

    // Writer thread only.
    private readonly List<PendingEntry> _pending = [];
    private ByteWriter? _encoder;
    private ITimer? _flushTimer;
    private long _batchGeneration;
    private OpId _lastAccepted;
    private OpId _lastCheckpoint;
    private Task _backupChain = Task.CompletedTask;

    private long _durableOpId;
    private volatile bool _faulted;
    private volatile bool _crashed;
    private int _disposed;

    /// <summary>Starts the writer thread on an open world; the writer owns <paramref name="db"/> from now on.</summary>
    /// <param name="db">The world; its schema must be current (open it after <see cref="WorldLoader.Load"/>).</param>
    /// <param name="options">Options.</param>
    /// <param name="time">Clock of the batches and backups.</param>
    /// <param name="faults">Failure injection; <see cref="NoFaults.Instance"/> in production.</param>
    /// <param name="logger">Logger.</param>
    public PersistenceWriter(WorldDatabase db, PersistenceOptions options, TimeProvider time, IPersistenceFaults faults, ILogger<PersistenceWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(faults);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxBatch, 1);
        _db = db;
        _options = options;
        _time = time;
        _faults = faults;
        _logger = logger;
        _backups = new BackupService(
            options.BackupsDirectory ?? Path.Combine(Path.GetDirectoryName(db.Path) ?? ".", "backups"), options.BackupsToKeep, time);
        _backupSource = WorldDatabase.ConnectionString(Path.GetFullPath(db.Path), SqliteOpenMode.ReadOnly, pooling: false);
        _channel = Channel.CreateUnbounded<WriteCommand>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // Read before the thread starts; from then on only the thread uses the connection.
        var (durable, lastCheckpoint) = ReadDurableProgress();
        _durableOpId = Sql.ToDb(durable.Value);
        _lastAccepted = durable;
        _lastCheckpoint = lastCheckpoint;

        _thread = new Thread(Run) { IsBackground = true, Name = "Horyzont persistence writer" };
        _thread.Start();
        if (options.BackupInterval > TimeSpan.Zero && options.BackupInterval != Timeout.InfiniteTimeSpan)
        {
            _backupTimer = time.CreateTimer(
                static state => ((PersistenceWriter)state!).Post(new BackupCommand("periodic", null)),
                this, options.BackupInterval, options.BackupInterval);
        }
    }

    /// <summary>Last operation known to be on disk (journal or checkpoint); readable from any thread, never decreases.</summary>
    public OpId DurableOpId => new(Sql.FromDb(Volatile.Read(ref _durableOpId)));

    /// <summary>True after a write error until the writer thread has executed <see cref="ClearFault"/> successfully.</summary>
    public bool IsFaulted => _faulted || _crashed;

    /// <summary>Enqueues a journal entry; never blocks. Entries must come with consecutive ids.</summary>
    /// <param name="entry">The entry.</param>
    /// <exception cref="ObjectDisposedException">The writer was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The entry could not be queued although the writer is neither disposed nor crashed (a disposal racing with this call, or a
    /// caller that enqueues after the shutdown order was broken): the entry would be lost without a trace.
    /// </exception>
    public void EnqueueJournal(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // After a simulated crash the channel is closed while not disposed; the entry is lost like in a dead process.
        // Any other closed channel means the entry would vanish silently (E3).
        if (!Post(new JournalCommand(entry, _time.GetTimestamp())) && !_crashed)
        {
            throw new InvalidOperationException("journal entry lost: writer is closing");
        }
    }

    /// <summary>
    /// Enqueues a checkpoint after every journal entry enqueued so far. The result is also reported as
    /// <see cref="CheckpointCommitted"/> or <see cref="CheckpointFailed"/>.
    /// </summary>
    /// <param name="request">The checkpoint.</param>
    /// <returns>True when committed; false when the write failed or the writer is faulted.</returns>
    /// <exception cref="ArgumentException">
    /// A terrain version has no payload with the same version, a payload key repeats, or a payload is not one of the captured versions.
    /// </exception>
    public Task<bool> CheckpointAsync(CheckpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        CheckTerrainPayloads(request);
        var completion = NewCompletion();
        if (!Post(new CheckpointCommand(request, completion)))
        {
            _events.Enqueue(new CheckpointFailed(request));
            completion.TrySetResult(false);
        }

        return completion.Task;
    }

    /// <summary>Commits everything enqueued before.</summary>
    /// <returns>True when every earlier entry is durable; false when the writer is faulted.</returns>
    public Task<bool> FlushAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var completion = NewCompletion();
        if (!Post(new FlushCommand(completion)))
        {
            completion.TrySetResult(false);
        }

        return completion.Task;
    }

    /// <summary>
    /// Copies the world after committing the pending journal (start of the server and every 6 h). The copy runs off the writer
    /// thread on its own read-only connection (a WAL snapshot), so journal batches keep committing; copies run one at a time.
    /// </summary>
    /// <param name="reason">Reason in the file name (ASCII letters, digits, '-').</param>
    /// <returns>True when the copy was made; false when the writer is faulted or the copy failed (the writer stays healthy).</returns>
    /// <exception cref="ArgumentException">The reason is empty or has another character.</exception>
    public Task<bool> BackupAsync(string reason)
    {
        BackupService.ValidateReason(reason);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var completion = NewCompletion();
        if (!Post(new BackupCommand(reason, completion)))
        {
            completion.TrySetResult(false);
        }

        return completion.Task;
    }

    /// <summary>
    /// After the cause of a fault is removed: the writer thread, when it reaches this command, writes the journal entries kept in
    /// memory in order and resumes normal work. Another error faults it again.
    /// </summary>
    public void ClearFault()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Post(new ClearFaultCommand());
    }

    /// <summary>Takes the next event; the server loop calls it every tick.</summary>
    /// <param name="evt">The event.</param>
    /// <returns>True when an event was taken.</returns>
    public bool TryDequeueEvent(out PersistenceEvent evt)
    {
        if (_events.TryDequeue(out var next))
        {
            evt = next;
            return true;
        }

        evt = null!;
        return false;
    }

    /// <summary>Stops accepting commands, executes the queue (with the journal batch) and closes the connection.</summary>
    /// <returns>A task that completes when the file is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _backupTimer?.Dispose();
        _channel.Writer.TryComplete();
        await _stopped.Task.ConfigureAwait(false);

        // Set only by the writer thread, which has stopped; a copy in progress finishes on its own connection.
        await _backupChain.ConfigureAwait(false);
        _flushTimer?.Dispose();
        _db.Dispose();
    }

    /// <summary>Completes the command channel without disposing or crashing the writer (tests of the lost-entry guard).</summary>
    internal void CompleteChannelForTest() => _channel.Writer.TryComplete();

    /// <summary>The backups of this writer (tests set <see cref="BackupService.BeforePublish"/>).</summary>
    internal BackupService Backups => _backups;

    /// <summary>Completes when the writer thread has executed every command enqueued before; commits nothing by itself (tests).</summary>
    internal Task ProbeAsync()
    {
        var completion = NewCompletion();
        if (!Post(new ProbeCommand(completion)))
        {
            completion.TrySetResult(false);
        }

        return completion.Task;
    }

    private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void CheckTerrainPayloads(CheckpointRequest request)
    {
        var payloads = new Dictionary<Core.World.Terrain.TerrainBlockKey, uint>();
        foreach (var payload in request.TerrainBlocks)
        {
            ArgumentNullException.ThrowIfNull(payload, nameof(request));
            ArgumentNullException.ThrowIfNull(payload.Data, nameof(request));
            if (!payloads.TryAdd(payload.Key, payload.Version))
            {
                throw new ArgumentException($"Terrain block {payload.Key} has two payloads.", nameof(request));
            }
        }

        foreach (var version in request.Core.TerrainVersions)
        {
            if (!payloads.Remove(version.Key, out var payloadVersion) || payloadVersion != version.Version)
            {
                throw new ArgumentException($"Terrain block {version.Key} version {version.Version} has no matching payload.", nameof(request));
            }
        }

        // A payload outside the captured versions is either older than the saved row (the version would go back) or newer than
        // the barrier (its operation stays in the journal and would be replayed on top of it).
        if (payloads.Count > 0)
        {
            var (key, version) = payloads.First();
            throw new ArgumentException($"Terrain block {key} version {version} is not a version captured by the checkpoint.", nameof(request));
        }
    }

    private bool Post(WriteCommand command) => _channel.Writer.TryWrite(command);

    private void Run()
    {
        WriteCommand? current = null;
        try
        {
            var reader = _channel.Reader;
            while (true)
            {
                if (reader.TryRead(out var command))
                {
                    current = command;
                    try
                    {
                        Execute(command);
                    }
                    catch (Exception e) when (e is not SimulatedCrashException)
                    {
                        // A bug outside the guarded writes: enter the fault state instead of killing the thread.
                        Fault(e);
                        Abandon(command);
                    }

                    current = null;
                    continue;
                }

                // Dedicated thread: blocking here does not hold any game or pool thread.
                if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    break;
                }
            }

            if (!_faulted)
            {
                CommitBatch();
            }
        }
        catch (SimulatedCrashException crash)
        {
            // Process death: no further write of any kind; the open transaction was rolled back by its disposal.
            _crashed = true;
            PersistenceLog.SimulatedCrash(_logger, crash.Point);
            if (current is not null)
            {
                Abandon(current);
            }
        }
        finally
        {
            _channel.Writer.TryComplete();
            while (_channel.Reader.TryRead(out var left))
            {
                Abandon(left);
            }

            _stopped.TrySetResult();
        }
    }

    private void Abandon(WriteCommand command)
    {
        switch (command)
        {
            case CheckpointCommand c:
                _events.Enqueue(new CheckpointFailed(c.Request));
                c.Completion.TrySetResult(false);
                break;
            case FlushCommand f:
                f.Completion.TrySetResult(false);
                break;
            case BackupCommand { Completion: { } b }:
                b.TrySetResult(false);
                break;
            case ProbeCommand p:
                p.Completion.TrySetResult(true);
                break;
            default:
                break;
        }
    }

    private void Execute(WriteCommand command)
    {
        switch (command)
        {
            case JournalCommand j:
                AcceptJournal(j);
                break;
            case FlushTimerCommand t:
                if (t.Generation == _batchGeneration && !_faulted)
                {
                    CommitBatch();
                }

                break;
            case CheckpointCommand c:
                if (!_faulted)
                {
                    CommitBatch();
                }

                if (_faulted)
                {
                    FailCheckpoint(c);
                }
                else
                {
                    WriteCheckpoint(c);
                }

                break;
            case FlushCommand f:
                if (!_faulted)
                {
                    CommitBatch();
                }

                f.Completion.TrySetResult(!_faulted);
                break;
            case BackupCommand b:
                Backup(b);
                break;
            case ClearFaultCommand:
                Recover();
                break;
            case ProbeCommand p:
                // Also after the backups requested before (they run off this thread).
                _backupChain.ContinueWith(
                    static (_, completion) => ((TaskCompletionSource<bool>)completion!).TrySetResult(true),
                    p.Completion,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                break;
            default:
                throw new InvalidOperationException($"Unknown command {command.GetType().Name}.");
        }
    }

    private void AcceptJournal(JournalCommand command)
    {
        var entry = command.Entry;
        if (entry.Id.Value > Sql.MaxOpId)
        {
            // Signed SQL would order it before every saved row; refuse it before it can be reported durable.
            Fault(new InvalidOperationException(
                $"Journal entry {entry.Id.Value} is above the supported ceiling {Sql.MaxOpId}; nothing after {_lastAccepted.Value} is written."));
            return;
        }

        if (entry.Id != _lastAccepted.Next)
        {
            // A gap or a repeat would break replay; refuse it and every later entry instead of writing it.
            Fault(new InvalidOperationException(
                $"Journal entry {entry.Id.Value} does not follow {_lastAccepted.Value}; nothing after {_lastAccepted.Value} is written."));
            return;
        }

        byte[] payload;
        try
        {
            _encoder ??= new ByteWriter();
            _encoder.Reset();
            OperationCodec.Write(_encoder, entry.Operation);
            payload = _encoder.WrittenSpan.ToArray();
        }
        catch (Exception e) when (e is not SimulatedCrashException)
        {
            Fault(e);
            return;
        }

        _lastAccepted = entry.Id;
        _pending.Add(new PendingEntry(entry, payload, Crc32.HashToUInt32(payload)));
        if (_faulted)
        {
            return;
        }

        if (_pending.Count >= _options.MaxBatch)
        {
            CommitBatch();
        }
        else if (_pending.Count == 1)
        {
            StartFlushTimer(command.EnqueuedAt);
        }
    }

    private void StartFlushTimer(long enqueuedAt)
    {
        var due = _options.FlushInterval - _time.GetElapsedTime(enqueuedAt);
        if (due <= TimeSpan.Zero)
        {
            CommitBatch();
            return;
        }

        _flushTimer?.Dispose();
        var generation = _batchGeneration;
        _flushTimer = _time.CreateTimer(
            state => Post(new FlushTimerCommand((long)state!)), generation, due, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Commits every pending entry in one transaction; on error the entries stay pending and the writer is faulted.</summary>
    private void CommitBatch()
    {
        _batchGeneration++;
        _flushTimer?.Dispose();
        _flushTimer = null;
        if (_pending.Count == 0)
        {
            return;
        }

        var connection = _db.Connection;
        try
        {
            using var transaction = connection.BeginTransaction();
            using var insert = Sql.Command(
                connection, transaction,
                "INSERT INTO journal (op_id, tick, kind, actor, payload, crc) VALUES ($op, $tick, $kind, $actor, $payload, $crc);");
            var op = insert.Parameters.Add("$op", SqliteType.Integer);
            var tick = insert.Parameters.Add("$tick", SqliteType.Integer);
            var kind = insert.Parameters.Add("$kind", SqliteType.Integer);
            var actor = insert.Parameters.Add("$actor", SqliteType.Integer);
            var payload = insert.Parameters.Add("$payload", SqliteType.Blob);
            var crc = insert.Parameters.Add("$crc", SqliteType.Integer);
            foreach (var pending in _pending)
            {
                op.Value = Sql.ToDb(pending.Entry.Id.Value);
                tick.Value = Sql.ToDb(pending.Entry.Tick.Value);
                kind.Value = (long)pending.Entry.Operation.Kind;
                actor.Value = pending.Entry.Actor is { } player ? Sql.ToDb(player.Value) : DBNull.Value;
                payload.Value = pending.Payload;
                crc.Value = (long)pending.Crc;
                insert.ExecuteNonQuery();
            }

            _faults.Hit(FaultPoint.BeforeJournalCommit);
            transaction.Commit();

            // An error reported here may come after the rows reached the disk: the outcome is unknown, so the entries stay
            // pending and are not acknowledged; ClearFault reads the disk and skips those already saved.
            _faults.Hit(FaultPoint.AfterJournalCommit);
        }
        catch (Exception e) when (e is not SimulatedCrashException)
        {
            Fault(e);
            return;
        }

        var upTo = _pending[^1].Entry.Id;
        _pending.Clear();
        Volatile.Write(ref _durableOpId, Sql.ToDb(upTo.Value));
        _events.Enqueue(new JournalCommitted(upTo));
    }

    private void WriteCheckpoint(CheckpointCommand command)
    {
        var checkpoint = command.Request.Core;
        var barrier = checkpoint.BarrierOpId;
        if (barrier > DurableOpId || barrier < _lastCheckpoint)
        {
            Fault(new InvalidOperationException(
                $"Checkpoint barrier {barrier.Value} is outside the durable journal ({_lastCheckpoint.Value}..{DurableOpId.Value})."));
            FailCheckpoint(command);
            return;
        }

        var connection = _db.Connection;
        try
        {
            using var transaction = connection.BeginTransaction();
            WriteConstructs(connection, transaction, checkpoint);
            _faults.Hit(FaultPoint.CheckpointMidTransaction);
            WritePlayers(connection, transaction, checkpoint);
            WriteMarkers(connection, transaction, checkpoint);
            WriteTerrain(connection, transaction, command.Request.TerrainBlocks);
            Sql.SetMeta(connection, transaction, Sql.MetaNextEntityId, Sql.ToDb(checkpoint.NextEntityId));
            Sql.SetMeta(connection, transaction, Sql.MetaNextPlayerId, Sql.ToDb(checkpoint.NextPlayerId));
            Sql.SetMeta(connection, transaction, Sql.MetaNextMarkerId, Sql.ToDb(checkpoint.NextMarkerId));
            Sql.SetMeta(connection, transaction, Sql.MetaLastCheckpointOpId, Sql.ToDb(barrier.Value));
            Sql.SetMeta(connection, transaction, Sql.MetaLastCheckpointTick, Sql.ToDb(checkpoint.Tick.Value));
            Sql.SetMeta(connection, transaction, Sql.MetaGameVersion, _options.GameVersion);
            using (var row = Sql.Command(
                connection, transaction, "INSERT INTO checkpoints (barrier_op_id, tick, created_utc) VALUES ($barrier, $tick, $created);"))
            {
                row.Parameters.AddWithValue("$barrier", Sql.ToDb(barrier.Value));
                row.Parameters.AddWithValue("$tick", Sql.ToDb(checkpoint.Tick.Value));
                row.Parameters.AddWithValue("$created", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
                row.ExecuteNonQuery();
            }

            using (var delete = Sql.Command(connection, transaction, "DELETE FROM journal WHERE op_id <= $barrier;"))
            {
                delete.Parameters.AddWithValue("$barrier", Sql.ToDb(barrier.Value));
                delete.ExecuteNonQuery();
            }

            _faults.Hit(FaultPoint.BeforeCheckpointCommit);
            transaction.Commit();
        }
        catch (Exception e) when (e is not SimulatedCrashException)
        {
            Fault(e);
            FailCheckpoint(command);
            return;
        }

        _lastCheckpoint = barrier;
        try
        {
            _faults.Hit(FaultPoint.AfterCheckpointCommit);
        }
        catch (Exception e) when (e is not SimulatedCrashException)
        {
            // The checkpoint is on disk: fault, then report it as committed (events before the task result).
            Fault(e);
            CompleteCheckpoint(command);
            return;
        }

        CompleteCheckpoint(command);
    }

    private static void WriteConstructs(SqliteConnection connection, SqliteTransaction transaction, Core.Checkpoints.WorldCheckpoint checkpoint)
    {
        foreach (var removed in checkpoint.RemovedConstructs)
        {
            using var command = Sql.Command(
                connection, transaction,
                "DELETE FROM construct_chunks WHERE construct_id = $id; DELETE FROM entities WHERE entity_id = $id;");
            command.Parameters.AddWithValue("$id", Sql.ToDb(removed.Value));
            command.ExecuteNonQuery();
        }

        foreach (var header in checkpoint.UpsertedConstructs)
        {
            using var command = Sql.Command(
                connection, transaction,
                "INSERT INTO entities (entity_id, kind, data) VALUES ($id, $kind, $data) "
                + "ON CONFLICT (entity_id) DO UPDATE SET kind = excluded.kind, data = excluded.data;");
            command.Parameters.AddWithValue("$id", Sql.ToDb(header.Id.Value));
            command.Parameters.AddWithValue("$kind", (long)EntityKind.Construct);
            command.Parameters.AddWithValue("$data", StateCodec.EncodeConstructHeaderBlob(header));
            command.ExecuteNonQuery();
        }

        foreach (var key in checkpoint.RemovedChunks)
        {
            using var command = Sql.Command(
                connection, transaction,
                "DELETE FROM construct_chunks WHERE construct_id = $id AND chunk_x = $x AND chunk_y = $y AND chunk_z = $z;");
            AddChunkKey(command, key);
            command.ExecuteNonQuery();
        }

        foreach (var chunk in checkpoint.UpsertedChunks)
        {
            using var command = Sql.Command(
                connection, transaction,
                "INSERT INTO construct_chunks (construct_id, chunk_x, chunk_y, chunk_z, data) VALUES ($id, $x, $y, $z, $data) "
                + "ON CONFLICT (construct_id, chunk_x, chunk_y, chunk_z) DO UPDATE SET data = excluded.data;");
            AddChunkKey(command, chunk.Key);
            command.Parameters.AddWithValue("$data", StateCodec.EncodeChunkBlob(chunk.Blocks));
            command.ExecuteNonQuery();
        }
    }

    private static void AddChunkKey(SqliteCommand command, Core.Checkpoints.ConstructChunkKey key)
    {
        command.Parameters.AddWithValue("$id", Sql.ToDb(key.Construct.Value));
        command.Parameters.AddWithValue("$x", (long)key.Chunk.X);
        command.Parameters.AddWithValue("$y", (long)key.Chunk.Y);
        command.Parameters.AddWithValue("$z", (long)key.Chunk.Z);
    }

    private static void WritePlayers(SqliteConnection connection, SqliteTransaction transaction, Core.Checkpoints.WorldCheckpoint checkpoint)
    {
        foreach (var player in checkpoint.UpsertedPlayers)
        {
            using var command = Sql.Command(
                connection, transaction,
                "INSERT INTO players (player_id, platform, platform_id, entity_id, data) VALUES ($id, $platform, $platformId, $entity, $data) "
                + "ON CONFLICT (player_id) DO UPDATE SET platform = excluded.platform, platform_id = excluded.platform_id, "
                + "entity_id = excluded.entity_id, data = excluded.data;");
            command.Parameters.AddWithValue("$id", Sql.ToDb(player.Player.Value));
            command.Parameters.AddWithValue("$platform", (long)player.Identity.Platform);
            command.Parameters.AddWithValue("$platformId", Sql.ToDb(player.Identity.Value));
            command.Parameters.AddWithValue("$entity", Sql.ToDb(player.Entity.Value));
            command.Parameters.AddWithValue("$data", StateCodec.EncodePlayerBlob(player));
            command.ExecuteNonQuery();
        }
    }

    private static void WriteMarkers(SqliteConnection connection, SqliteTransaction transaction, Core.Checkpoints.WorldCheckpoint checkpoint)
    {
        foreach (var marker in checkpoint.UpsertedMarkers)
        {
            using var command = Sql.Command(
                connection, transaction,
                "INSERT INTO markers (marker_id, data) VALUES ($id, $data) ON CONFLICT (marker_id) DO UPDATE SET data = excluded.data;");
            command.Parameters.AddWithValue("$id", Sql.ToDb(marker.Id.Value));
            command.Parameters.AddWithValue("$data", StateCodec.EncodeMarkerBlob(marker));
            command.ExecuteNonQuery();
        }

        foreach (var removed in checkpoint.RemovedMarkers)
        {
            using var command = Sql.Command(connection, transaction, "DELETE FROM markers WHERE marker_id = $id;");
            command.Parameters.AddWithValue("$id", Sql.ToDb(removed.Value));
            command.ExecuteNonQuery();
        }
    }

    private static void WriteTerrain(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<TerrainBlockPayload> blocks)
    {
        foreach (var block in blocks)
        {
            using var command = Sql.Command(
                connection, transaction,
                "INSERT INTO terrain_blocks (planet_id, lod, block_x, block_y, block_z, version, data) VALUES ($p, $lod, $x, $y, $z, $version, $data) "
                + "ON CONFLICT (planet_id, lod, block_x, block_y, block_z) DO UPDATE SET version = excluded.version, data = excluded.data;");
            command.Parameters.AddWithValue("$p", Sql.ToDb(block.Key.Planet.Value));
            command.Parameters.AddWithValue("$lod", (long)block.Key.Lod);
            command.Parameters.AddWithValue("$x", (long)block.Key.X);
            command.Parameters.AddWithValue("$y", (long)block.Key.Y);
            command.Parameters.AddWithValue("$z", (long)block.Key.Z);
            command.Parameters.AddWithValue("$version", (long)block.Version);
            command.Parameters.AddWithValue("$data", block.Data);
            command.ExecuteNonQuery();
        }
    }

    private void CompleteCheckpoint(CheckpointCommand command)
    {
        PersistenceLog.CheckpointCommitted(_logger, command.Request.Core.BarrierOpId.Value);
        _events.Enqueue(new CheckpointCommitted(command.Request));
        command.Completion.TrySetResult(true);
    }

    private void FailCheckpoint(CheckpointCommand command)
    {
        PersistenceLog.CheckpointFailed(_logger, command.Request.Core.BarrierOpId.Value);
        _events.Enqueue(new CheckpointFailed(command.Request));
        command.Completion.TrySetResult(false);
    }

    private void Backup(BackupCommand command)
    {
        if (!_faulted)
        {
            CommitBatch();
        }

        if (_faulted)
        {
            command.Completion?.TrySetResult(false);
            return;
        }

        // E2: a copy of a large world takes seconds; it runs on the thread pool, chained after the previous copy, while this
        // thread keeps committing journal batches. The batch committed above is in the copy's snapshot.
        _backupChain = _backupChain.ContinueWith(
            static (_, state) =>
            {
                var (writer, backup) = ((PersistenceWriter, BackupCommand))state!;
                writer.CopyWorld(backup);
            },
            (this, command),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    /// <summary>Makes one copy from a read-only connection; never throws and never faults the writer (E1).</summary>
    private void CopyWorld(BackupCommand command)
    {
        try
        {
            using var source = WorldDatabase.OpenConnection(_backupSource, readOnly: true, _logger);
            var path = _backups.CreateBackup(source, command.Reason);
            PersistenceLog.BackupCreated(_logger, path, command.Reason);
            command.Completion?.TrySetResult(true);
        }
        catch (Exception e) when (e is not SimulatedCrashException)
        {
            // Not swallowed: logged and reported as false. A failed copy does not touch the world file, so the writer stays healthy.
            PersistenceLog.BackupFailed(_logger, e, command.Reason);
        }
        finally
        {
            // No-op after success; otherwise the caller sees false.
            command.Completion?.TrySetResult(false);
        }
    }

    private void Recover()
    {
        if (!_faulted)
        {
            return;
        }

        OpId durable;
        try
        {
            (durable, _lastCheckpoint) = ReadDurableProgress();
        }
        catch (Exception e) when (e is not SimulatedCrashException)
        {
            Fault(e);
            return;
        }

        // A commit that failed after reaching the disk must not be written twice.
        var skip = 0;
        while (skip < _pending.Count && _pending[skip].Entry.Id <= durable)
        {
            skip++;
        }

        if (skip > 0)
        {
            PersistenceLog.SkippedDurable(_logger, _pending[skip - 1].Entry.Id.Value);
            _pending.RemoveRange(0, skip);
        }

        if (durable > DurableOpId)
        {
            // Entries of a commit with an unknown outcome are on disk after all: acknowledge them now.
            Volatile.Write(ref _durableOpId, Sql.ToDb(durable.Value));
            _events.Enqueue(new JournalCommitted(durable));
        }

        if (_pending.Count > 0 ? _pending[0].Entry.Id != durable.Next : _lastAccepted < durable)
        {
            Fault(new InvalidOperationException(
                $"Journal in memory does not continue the disk (durable {durable.Value}, accepted {_lastAccepted.Value})."));
            return;
        }

        _faulted = false;
        if (_pending.Count > 0)
        {
            PersistenceLog.FaultCleared(_logger, _pending.Count, _pending[0].Entry.Id.Value);
            CommitBatch();
        }
    }

    /// <summary>Highest operation on disk: the last journal row or, with an empty journal, the last checkpoint barrier.</summary>
    private (OpId Durable, OpId LastCheckpoint) ReadDurableProgress()
    {
        var connection = _db.Connection;
        var lastCheckpoint = Sql.OpIdFromDb((long)(WorldDatabase.ExecuteScalar(
            connection, $"SELECT value FROM meta WHERE key = '{Sql.MetaLastCheckpointOpId}';") ?? 0L));
        if (WorldDatabase.ExecuteScalar(connection, "SELECT MIN(op_id) FROM journal;") is long min)
        {
            // A negative id would hide from MAX; the loader rejects it as damage, so the writer does too.
            _ = Sql.OpIdFromDb(min);
        }

        var journalMax = WorldDatabase.ExecuteScalar(connection, "SELECT MAX(op_id) FROM journal;");
        var durable = journalMax is long max ? Math.Max(Sql.OpIdFromDb(max), lastCheckpoint) : lastCheckpoint;
        return (new OpId(durable), new OpId(lastCheckpoint));
    }

    private void Fault(Exception error)
    {
        _faulted = true;
        _batchGeneration++;
        _flushTimer?.Dispose();
        _flushTimer = null;
        PersistenceLog.WriterFaulted(_logger, error, DurableOpId.Value, _pending.Count);
        _events.Enqueue(new WriterFaulted(error));
    }

    private readonly record struct PendingEntry(JournalEntry Entry, byte[] Payload, uint Crc);

    private abstract record WriteCommand;

    private sealed record JournalCommand(JournalEntry Entry, long EnqueuedAt) : WriteCommand;

    private sealed record FlushTimerCommand(long Generation) : WriteCommand;

    private sealed record CheckpointCommand(CheckpointRequest Request, TaskCompletionSource<bool> Completion) : WriteCommand;

    private sealed record FlushCommand(TaskCompletionSource<bool> Completion) : WriteCommand;

    private sealed record BackupCommand(string Reason, TaskCompletionSource<bool>? Completion) : WriteCommand;

    private sealed record ClearFaultCommand : WriteCommand;

    private sealed record ProbeCommand(TaskCompletionSource<bool> Completion) : WriteCommand;
}
