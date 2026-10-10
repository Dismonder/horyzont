using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Godot;

using Horyzont.Constructs.Meshing;
using Horyzont.Constructs.Scheduling;
using Horyzont.Constructs.Sources;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Constructs;

/// <summary>
/// Makes constructs visible and physical: one <see cref="ConstructNode"/> per construct in range, one
/// <see cref="ConstructChunkNode"/> per non-empty chunk. Chunks are meshed on worker threads from snapshots copied on the main
/// thread; results are applied in a per-frame time budget. Everything except the workers runs on the main thread.
/// Leaving the scene tree permanently shuts this world down, including during reparenting. The composition root must not
/// move an initialized world with RemoveChild/AddChild; reattaching it does not restart the workers.
/// </summary>
public sealed partial class ConstructWorld : Node3D
{
    private const int RangeUpdateInterval = 30;

    private readonly Dictionary<EntityId, Entry> _entries = [];
    private readonly HashSet<ConstructChunkKey> _retryChunks = [];
    private readonly List<ConstructChunkKey> _retryScratch = [];
    private readonly Dictionary<EntityId, double> _distanceSquared = [];
    private readonly List<System.Numerics.Vector3> _viewers = [];
    private readonly List<ConstructChunkKey> _dirtyScratch = [];
    private readonly List<EntityId> _createdScratch = [];
    private readonly List<EntityId> _removedScratch = [];
    private readonly List<EntityId> _reposedScratch = [];
    private readonly List<EntityId> _constructScratch = [];
    private readonly List<ChunkPos> _chunkScratch = [];
    private readonly List<EntityId> _unloadScratch = [];
    private readonly Queue<ConstructChunkKey> _collisionOrder = new();
    private readonly Dictionary<ConstructChunkKey, (RemeshJob Job, Vector3[] Points)> _collisionPoints = [];

    private IConstructSource _source = null!;
    private BlockVisuals? _visuals;
    private ConstructWorldOptions _options = new();
    private ILogger<ConstructWorld> _logger = null!;
    private ChunkRemeshQueue _queue = null!;
    private MeshWorkerPool? _pool;
    internal Action<MeshJobSlot>? BeforeBuild { get; set; }

    private FrameRef _frame;
    private bool _frameFixed;
    private readonly HashSet<EntityId> _presentScratch = new();
    private bool _initialized;
    private bool _rangeDirty;
    private int _framesSinceRangeUpdate;
    private Task? _shutdownTask;

    /// <summary>
    /// Raised on the main thread after a chunk's mesh and collision were swapped (or removed because the chunk became empty). The
    /// collision shape is built in a step after the mesh step, usually in a later frame; this fires when both are in place.
    /// Also raised after two worker failures for the current edit, with the old mesh and collision kept.
    /// </summary>
    public event Action<EntityId, ChunkPos>? ChunkMeshApplied;

    /// <summary>Main-thread time spent applying mesher results (diagnostics for the frame-time tests).</summary>
    public ApplyTimings Timings { get; } = new();

    /// <summary>Rebuilds waiting, running or finished but not yet applied.</summary>
    public int PendingJobs => (_queue?.Pending ?? 0) + _collisionOrder.Count;

    /// <summary>True after <see cref="ShutdownAsync"/> was called.</summary>
    public bool IsShutdown => _shutdownTask is not null;

    /// <summary>Mesher threads that are still running (0 after <see cref="ShutdownAsync"/> completed).</summary>
    public int AliveWorkerThreads => _pool?.AliveThreads ?? 0;

    /// <summary>Number of constructs that currently have a node.</summary>
    public int LoadedConstructCount => _entries.Count;

    /// <summary>Whether all construct chunks near a saved pose have installed their current collision result.</summary>
    /// <param name="frame">The saved planet frame.</param>
    /// <param name="position">Saved feet position in that frame.</param>
    /// <param name="radius">Radius enclosing the support search.</param>
    /// <returns>False during loading, edits, failed builds or pending collision uploads.</returns>
    public bool AreCollisionsReady(FrameRef frame, System.Numerics.Vector3 position, float radius)
    {
        if (!_initialized || IsShutdown || frame != _frame || _rangeDirty)
        {
            return false;
        }

        _constructScratch.Clear();
        _source.CollectConstructs(frame, _constructScratch);
        foreach (var id in _constructScratch)
        {
            if (!_source.TryGetPose(id, out var pose))
            {
                continue;
            }

            var low = new System.Numerics.Vector3(pose.BoundsMin.X, pose.BoundsMin.Y, pose.BoundsMin.Z);
            var high = new System.Numerics.Vector3(pose.BoundsMax.X + 1, pose.BoundsMax.Y + 1, pose.BoundsMax.Z + 1);
            var centre = pose.Origin + System.Numerics.Vector3.Transform((low + high) / 2, pose.Rotation);
            if ((position - centre).Length() > radius + ((high - low).Length() / 2))
            {
                continue;
            }

            _chunkScratch.Clear();
            _source.CollectChunks(id, _chunkScratch);
            if (!_entries.TryGetValue(id, out var entry))
            {
                if (_chunkScratch.Count > 0)
                {
                    return false;
                }

                continue;
            }

            if (!entry.Collision && _chunkScratch.Count > 0)
            {
                return false;
            }

            foreach (var chunk in _chunkScratch)
            {
                if (!entry.CollisionReadyChunks.Contains(chunk))
                {
                    return false;
                }
            }

            foreach (var chunk in entry.KnownChunks)
            {
                if (!entry.CollisionReadyChunks.Contains(chunk))
                {
                    return false; // includes an emptied chunk whose old shape is still waiting to be removed
                }
            }
        }

        return true;
    }

    /// <summary>Connects the world to its inputs and starts the workers.</summary>
    /// <param name="source">Where constructs are read from (main thread only).</param>
    /// <param name="table">Render table of the world session.</param>
    /// <param name="visuals">Block materials; null in <see cref="ConstructWorldMode.CollisionOnly"/>.</param>
    /// <param name="options">Settings.</param>
    /// <param name="logger">Logger.</param>
    public void Initialize(
        IConstructSource source, BlockRenderTable table, BlockVisuals? visuals, ConstructWorldOptions options, ILogger<ConstructWorld> logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        if (_initialized)
        {
            throw new InvalidOperationException("ConstructWorld is already initialized.");
        }

        if (options.Mode == ConstructWorldMode.RenderAndCollision && visuals is null)
        {
            throw new ArgumentException("Visuals are required unless the mode is CollisionOnly.", nameof(visuals));
        }

        _source = source;
        _visuals = options.Mode == ConstructWorldMode.CollisionOnly ? null : visuals;
        _options = options;
        _logger = logger;
        _queue = new ChunkRemeshQueue(options.MaxInFlightJobs) { Distance = DistanceOf };
        _pool = new MeshWorkerPool(table, options.WorkerCount, options.MaxInFlightJobs, logger, BeforeBuild, options.TimeProvider);
        _initialized = true;
        if (table.MissingKeyCount > 0)
        {
            LogMissingBlockKeys(table.MissingKeyCount, string.Join(", ", table.UnknownKeys));
        }
    }

    /// <summary>Main thread, after each applied operation (server: AppliedOperation; client: OperationApplied in K3-15).</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="changes">Its construct changes.</param>
    public void OnApplied(Operation operation, IReadOnlyList<ConstructChange> changes)
    {
        if (!_initialized || IsShutdown)
        {
            return;
        }

        _dirtyScratch.Clear();
        _createdScratch.Clear();
        _removedScratch.Clear();
        _reposedScratch.Clear();
        ConstructDirtyChunks.Collect(operation, changes, _dirtyScratch, _createdScratch, _removedScratch, _reposedScratch);
        foreach (var id in _removedScratch)
        {
            Unload(id);
        }

        if (!_frameFixed && operation is ConstructCreateOp create)
        {
            _frame = create.Parent; // only until RebuildAll names the shown frame; later creates must not switch it
        }

        foreach (var id in _createdScratch)
        {
            if (!_entries.ContainsKey(id) && TryEvaluate(id, out var distance, out var collision) && IsInLoadRange(distance, null))
            {
                Load(id, distance, collision, RemeshPriority.Edit);
            }
        }

        foreach (var key in _dirtyScratch)
        {
            if (_entries.TryGetValue(key.Construct, out var entry) &&
                (_source.TryGetChunkBlocks(key.Construct, key.Chunk, out _) || entry.KnownChunks.Contains(key.Chunk)))
            {
                MarkDirty(key, RemeshPriority.Edit);
            }
        }

        foreach (var change in changes)
        {
            RefreshPose(change.Construct);
        }

        foreach (var id in _reposedScratch)
        {
            RefreshMembership(id);
        }
    }

    /// <summary>After world load/replay or resync: rebuild every construct of the frame.</summary>
    /// <param name="frame">The frame whose constructs are shown.</param>
    public void RebuildAll(FrameRef frame)
    {
        if (!_initialized || IsShutdown)
        {
            return;
        }

        _frame = frame;
        _frameFixed = true;
        _unloadScratch.Clear();
        _unloadScratch.AddRange(_entries.Keys);
        foreach (var id in _unloadScratch)
        {
            Unload(id);
        }

        _retryChunks.Clear();
        _queue.Clear();
        UpdateRanges(RemeshPriority.Rebuild);
    }

    /// <summary>Viewer positions in the planet frame (players, cameras). No viewers means no interest unless the explicit tools mode is enabled.</summary>
    /// <param name="planetFramePositions">Positions.</param>
    public void SetViewers(ReadOnlySpan<System.Numerics.Vector3> planetFramePositions)
    {
        _viewers.Clear();
        foreach (var position in planetFramePositions)
        {
            _viewers.Add(position);
        }

        _rangeDirty = true;
    }

    /// <summary>Finds the node of a construct.</summary>
    /// <param name="id">Construct entity.</param>
    /// <param name="node">The node when the construct is loaded.</param>
    /// <returns>True when loaded.</returns>
    public bool TryGetConstructNode(EntityId id, [NotNullWhen(true)] out Node3D? node)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            node = entry.Node;
            return true;
        }

        node = null;
        return false;
    }

    /// <summary>Finds the typed node of a construct.</summary>
    /// <param name="id">Construct entity.</param>
    /// <param name="node">The node when the construct is loaded.</param>
    /// <returns>True when loaded.</returns>
    public bool TryGetConstruct(EntityId id, [NotNullWhen(true)] out ConstructNode? node)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            node = entry.Node;
            return true;
        }

        node = null;
        return false;
    }

    /// <summary>Idempotent: stops accepting work, joins the workers within the timeout and drops their results.</summary>
    /// <returns>A task that completes when the workers have stopped (or the timeout elapsed).</returns>
    public Task ShutdownAsync()
    {
        if (_shutdownTask is not null)
        {
            return _shutdownTask;
        }

        if (!_initialized)
        {
            _shutdownTask = Task.CompletedTask;
            return _shutdownTask;
        }

        _retryChunks.Clear();
        _queue.Clear();
        _collisionPoints.Clear();
        _collisionOrder.Clear();
        var pool = _pool!;
        pool.Cancel(); // from now on the main thread neither starts nor applies anything
        var timeout = _options.ShutdownTimeout;
        _shutdownTask = Task.Run(() =>
        {
            pool.Shutdown(timeout);
            pool.Dispose();
        });
        return _shutdownTask;
    }

    /// <summary>Runs one step of the per-frame work: ranges, applying finished meshes in the time budget, starting new jobs.</summary>
    /// <param name="budgetMilliseconds">Apply budget; at least one result is applied.</param>
    public void Pump(double budgetMilliseconds)
    {
        if (!_initialized || IsShutdown)
        {
            return;
        }

        if (_rangeDirty || ++_framesSinceRangeUpdate >= RangeUpdateInterval)
        {
            UpdateRanges(RemeshPriority.Rebuild);
        }

        ApplyResults(budgetMilliseconds);
        StartJobs();
    }

    /// <inheritdoc />
    public override void _Process(double delta) => Pump(_options.MaxApplyMillisecondsPerFrame);

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _ = ShutdownAsync();
    }

    private double DistanceOf(EntityId id) => _distanceSquared.GetValueOrDefault(id);

    private void ApplyResults(double budgetMilliseconds)
    {
        // One unit is a mesh upload (a finished job) or the build of one collision shape. Units run until the budget is spent, but at
        // least one runs per frame; collision shapes go first so a chunk is complete as soon as possible.
        var pool = _pool!;
        var started = Stopwatch.GetTimestamp();
        do
        {
            if (_collisionOrder.Count > 0)
            {
                ApplyNextCollision();
            }
            else if (pool.TryTakeResult(out var slot))
            {
                var current = _queue.Complete(slot.Job);
                if (current)
                {
                    if (slot.Failure is null)
                    {
                        _retryChunks.Remove(slot.Job.Key);
                        ApplySlot(slot);
                    }
                    else if (_retryChunks.Add(slot.Job.Key))
                    {
                        // A normal edit clears this retry budget and invalidates the running generation.
                        _queue.MarkDirty(slot.Job.Key, slot.Job.Priority);
                    }
                    else
                    {
                        _retryChunks.Remove(slot.Job.Key);
                        var key = slot.Job.Key;
                        LogMeshingRetryFailed(slot.Failure, key.Construct.Value, key.Chunk.X, key.Chunk.Y, key.Chunk.Z);
                        ChunkMeshApplied?.Invoke(key.Construct, key.Chunk);
                    }
                }

                pool.ReturnSlot(slot);
            }
            else
            {
                break;
            }
        }
        while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < budgetMilliseconds);
    }

    private void StartJobs()
    {
        var pool = _pool!;
        while (pool.HasFreeSlot && _queue.TryStartNext(out var job))
        {
            if (!_entries.TryGetValue(job.Key.Construct, out var entry))
            {
                _queue.Complete(job);
                continue;
            }

            pool.TryRentSlot(out var slot);
            if (!slot!.Input.Fill(_source, job.Key, job.Generation))
            {
                // The chunk is empty or gone: no mesh work, apply the empty result right away.
                if (_queue.Complete(job))
                {
                    _retryChunks.Remove(job.Key);
                    ApplyEmpty(job.Key);
                }

                pool.ReturnSlot(slot);
                continue;
            }

            slot.Job = job;
            slot.WantsMesh = _visuals is not null;
            slot.WantsCollision = entry.Collision;
            pool.Submit(slot);
        }
    }

    private void ApplySlot(MeshJobSlot slot)
    {
        var key = slot.Job.Key;
        if (!_entries.TryGetValue(key.Construct, out var entry))
        {
            return;
        }

        var uploadStart = Stopwatch.GetTimestamp();
        var prepared = slot.Prepared;
        if (prepared is null)
        {
            return;
        }

        // The worker copied the arrays; points are only missing when collision was switched on after the job started.
        var points = entry.Collision ? prepared.Points ?? (prepared.HasCollisionFaces ? ChunkMeshUploader.CopyCollisionPoints(slot.Output) : null) : null;

        var hasMesh = _visuals is not null && prepared.HasMesh;
        if (!hasMesh && points is null)
        {
            ApplyEmpty(key);
            return;
        }

        var chunk = entry.Node.GetOrCreateChunk(key.Chunk);
        chunk.ApplyMesh(prepared.Cubes, prepared.Shapes, _visuals);
        Timings.AddUpload(uploadStart);
        if (points is null)
        {
            _collisionPoints.Remove(key); // a queued older shape would be stale (the chunk lost its collision faces)
            chunk.ClearCollision();
            entry.CollisionReadyChunks.Add(key.Chunk); // current geometry intentionally has no collision faces
            ChunkMeshApplied?.Invoke(key.Construct, key.Chunk);
            return;
        }

        if (_collisionPoints.TryAdd(key, (slot.Job, points)))
        {
            _collisionOrder.Enqueue(key);
        }
        else
        {
            _collisionPoints[key] = (slot.Job, points); // keeps its place in the queue, newest data wins
        }
    }

    private void ApplyNextCollision()
    {
        var key = _collisionOrder.Dequeue();
        if (!_collisionPoints.Remove(key, out var result) || !_queue.IsCurrent(result.Job) || !_entries.TryGetValue(key.Construct, out var entry) || !entry.Collision)
        {
            return; // the construct was unloaded, or left the collision range, while the shape waited
        }

        var collisionStart = Stopwatch.GetTimestamp();
        entry.Node.GetOrCreateChunk(key.Chunk).ApplyCollision(result.Points);
        entry.CollisionReadyChunks.Add(key.Chunk);
        Timings.AddCollision(collisionStart);
        ChunkMeshApplied?.Invoke(key.Construct, key.Chunk);
    }

    private void ApplyEmpty(ConstructChunkKey key)
    {
        _collisionPoints.Remove(key);
        if (_entries.TryGetValue(key.Construct, out var entry) && entry.KnownChunks.Contains(key.Chunk))
        {
            entry.Node.RemoveChunk(key.Chunk);
            if (!_source.TryGetChunkBlocks(key.Construct, key.Chunk, out _))
            {
                entry.KnownChunks.Remove(key.Chunk);
                entry.CollisionReadyChunks.Remove(key.Chunk);
            }
            else
            {
                entry.CollisionReadyChunks.Add(key.Chunk); // a current non-colliding chunk is also ready
            }

            ChunkMeshApplied?.Invoke(key.Construct, key.Chunk);
        }
    }

    private void UpdateRanges(RemeshPriority priority)
    {
        _rangeDirty = false;
        _framesSinceRangeUpdate = 0;
        _constructScratch.Clear();
        _source.CollectConstructs(_frame, _constructScratch);
        _distanceSquared.Clear();
        foreach (var id in _constructScratch)
        {
            if (!TryEvaluate(id, out var distance, out var wantsCollision))
            {
                continue;
            }

            _distanceSquared[id] = distance * distance;
            if (_entries.TryGetValue(id, out var entry))
            {
                if (!IsInLoadRange(distance, entry))
                {
                    Unload(id);
                    continue;
                }

                UpdateCollision(id, entry, distance);
            }
            else if (IsInLoadRange(distance, null))
            {
                Load(id, distance, wantsCollision, priority);
            }
        }

        // Constructs that vanished from the source without an operation (resync) lose their nodes.
        _presentScratch.Clear();
        foreach (var id in _constructScratch)
        {
            _presentScratch.Add(id);
        }

        _unloadScratch.Clear();
        foreach (var id in _entries.Keys)
        {
            if (!_presentScratch.Contains(id))
            {
                _unloadScratch.Add(id);
            }
        }

        foreach (var id in _unloadScratch)
        {
            Unload(id);
        }
    }

    private void UpdateCollision(EntityId id, Entry entry, double distance)
    {
        var radius = _options.CollisionRadiusMeters + (entry.Collision ? _options.HysteresisMeters : 0f);
        var wants = ConstructRange.Includes(_viewers.Count, _options.LoadAllWithoutViewers, distance, radius);
        if (wants == entry.Collision)
        {
            return;
        }

        entry.Collision = wants;
        if (wants)
        {
            MarkAllChunks(id, RemeshPriority.Rebuild); // the collision data was not kept
        }
        else
        {
            entry.Node.ClearCollision();
        }
    }

    private bool TryEvaluate(EntityId id, out double distance, out bool collision)
    {
        distance = 0;
        collision = _viewers.Count > 0 || _options.LoadAllWithoutViewers;
        if (!_source.TryGetPose(id, out var pose) || pose.Parent != _frame)
        {
            return false; // gone, or in a frame that is not the shown one
        }

        if (_viewers.Count > 0)
        {
            var low = new System.Numerics.Vector3(pose.BoundsMin.X, pose.BoundsMin.Y, pose.BoundsMin.Z);
            var high = new System.Numerics.Vector3(pose.BoundsMax.X + 1, pose.BoundsMax.Y + 1, pose.BoundsMax.Z + 1);
            var centreLocal = (low + high) / 2;
            var radius = (high - low).Length() / 2;
            var centre = pose.Origin + System.Numerics.Vector3.Transform(centreLocal, pose.Rotation);
            distance = double.MaxValue;
            foreach (var viewer in _viewers)
            {
                distance = Math.Min(distance, Math.Max(0, (viewer - centre).Length() - radius));
            }

            collision = distance <= _options.CollisionRadiusMeters;
        }

        return true;
    }

    private bool IsInLoadRange(double distance, Entry? loaded)
    {
        var radius = _options.Mode == ConstructWorldMode.CollisionOnly ? _options.CollisionRadiusMeters : _options.RenderRadiusMeters;
        return ConstructRange.Includes(_viewers.Count, _options.LoadAllWithoutViewers, distance, radius + (loaded is null ? 0f : _options.HysteresisMeters));
    }

    private void Load(EntityId id, double distance, bool collision, RemeshPriority priority)
    {
        if (!_source.TryGetPose(id, out var pose))
        {
            return;
        }

        var node = new ConstructNode(id);
        node.SetPose(pose);
        AddChild(node);
        _entries[id] = new Entry(node) { Collision = collision };
        _distanceSquared[id] = distance * distance;
        MarkAllChunks(id, priority);
    }

    private void Unload(EntityId id)
    {
        if (!_entries.Remove(id, out var entry))
        {
            return;
        }

        _queue.RemoveConstruct(id);
        _retryScratch.Clear();
        foreach (var key in _retryChunks)
        {
            if (key.Construct == id)
            {
                _retryScratch.Add(key);
            }
        }

        foreach (var key in _retryScratch)
        {
            _retryChunks.Remove(key);
        }
        var pendingCollisions = _collisionOrder.Count;
        for (var i = 0; i < pendingCollisions; i++)
        {
            var key = _collisionOrder.Dequeue();
            if (key.Construct == id)
            {
                _collisionPoints.Remove(key);
            }
            else
            {
                _collisionOrder.Enqueue(key);
            }
        }

        _distanceSquared.Remove(id);
        RemoveChild(entry.Node);
        entry.Node.Free();
    }

    private void MarkAllChunks(EntityId id, RemeshPriority priority)
    {
        _chunkScratch.Clear();
        _source.CollectChunks(id, _chunkScratch);
        foreach (var chunk in _chunkScratch)
        {
            MarkDirty(new ConstructChunkKey(id, chunk), priority);
        }
    }

    private void RefreshPose(EntityId id)
    {
        if (_entries.TryGetValue(id, out var entry) && _source.TryGetPose(id, out var pose))
        {
            entry.Node.SetPose(pose);
        }
    }

    private void RefreshMembership(EntityId id)
    {
        _entries.TryGetValue(id, out var entry);
        if (!TryEvaluate(id, out var distance, out var collision) || !IsInLoadRange(distance, entry))
        {
            Unload(id);
            return;
        }

        if (entry is null)
        {
            Load(id, distance, collision, RemeshPriority.Edit);
        }
        else
        {
            _distanceSquared[id] = distance * distance;
            RefreshPose(id);
            UpdateCollision(id, entry, distance);
        }
    }

    private void MarkDirty(ConstructChunkKey key, RemeshPriority priority)
    {
        _retryChunks.Remove(key);
        if (_entries.TryGetValue(key.Construct, out var entry))
        {
            entry.CollisionReadyChunks.Remove(key.Chunk);
            if (_source.TryGetChunkBlocks(key.Construct, key.Chunk, out _))
            {
                entry.KnownChunks.Add(key.Chunk); // keep until an emptied chunk completes, even before its first mesh upload
            }
        }

        _queue.MarkDirty(key, priority);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Meshing construct {Construct} chunk ({X}, {Y}, {Z}) failed again; keeping the old mesh and completing waiters")]
    private partial void LogMeshingRetryFailed(Exception exception, ulong construct, int x, int y, int z);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} block type key(s) in the world are unknown to the game content and are drawn as core:missing: {Keys}")]
    private partial void LogMissingBlockKeys(int count, string keys);

    private sealed class Entry(ConstructNode node)
    {
        public ConstructNode Node { get; } = node;

        public bool Collision { get; set; }

        public HashSet<ChunkPos> KnownChunks { get; } = [];

        public HashSet<ChunkPos> CollisionReadyChunks { get; } = [];
    }
}
