using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Horyzont.Constructs.Meshing;
using Horyzont.Constructs.Scheduling;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Constructs;

/// <summary>One reusable unit of mesher work: the input snapshot, the output buffers and the job that owns them.</summary>
internal sealed class MeshJobSlot : IDisposable
{
    public ChunkMeshInput Input { get; } = new();

    public ChunkMeshData Output { get; } = new();

    public RemeshJob Job { get; set; }

    /// <summary>Set on the main thread before submitting: the worker also builds the render arrays.</summary>
    public bool WantsMesh { get; set; }

    /// <summary>Set on the main thread before submitting: the worker also builds the collision shape.</summary>
    public bool WantsCollision { get; set; }

    /// <summary>Godot arrays and points built by the worker from <see cref="Output"/> (null until then, and after the main thread took them).</summary>
    public ChunkMeshUploader.PreparedChunk? Prepared { get; set; }

    /// <summary>The exception thrown by the mesher on a worker thread, or null.</summary>
    public Exception? Failure { get; set; }

    public void Dispose()
    {
        Prepared?.Dispose();
        Prepared = null;
        Output.Dispose();
    }
}

/// <summary>
/// Worker threads for the chunk mesher. Workers touch only the slot they were handed (a snapshot copied on the main thread) and the
/// immutable render table; they never see <c>WorldState</c> or nodes. Slots circulate: main thread → job queue → worker → result
/// queue → main thread. Both queues are bounded by the slot count.
/// </summary>
internal sealed partial class MeshWorkerPool : IDisposable
{
    private readonly TimeProvider _time;
    private readonly BlockRenderTable _table;
    private readonly ILogger _logger;
    private readonly BlockingCollection<MeshJobSlot> _jobs;
    private readonly ConcurrentQueue<MeshJobSlot> _results = new();
    private readonly Stack<MeshJobSlot> _free = new();
    private readonly List<MeshJobSlot> _allSlots = [];
    private readonly Thread[] _threads;
    private readonly CancellationTokenSource _cancel = new();
    private volatile bool _shutdown;
    private bool _slotsDisposed;
    private readonly object _lifecycleLock = new();
    private int _remainingWorkers;
    private bool _disposeRequested;
    private readonly Action<MeshJobSlot>? _beforeBuild;
    internal int CleanupCount { get; private set; }

    public MeshWorkerPool(BlockRenderTable table, int workerCount, int slotCount, ILogger logger, Action<MeshJobSlot>? beforeBuild = null, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCount, 1);
        _time = time ?? TimeProvider.System;
        _table = table;
        _logger = logger;
        _beforeBuild = beforeBuild;
        _remainingWorkers = workerCount;
        _jobs = new BlockingCollection<MeshJobSlot>(slotCount);
        for (var i = 0; i < slotCount; i++)
        {
            var slot = new MeshJobSlot();
            _allSlots.Add(slot);
            _free.Push(slot);
        }

        _threads = new Thread[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            _threads[i] = new Thread(WorkerLoop) { Name = $"ConstructMesher{i}", IsBackground = true };
            _threads[i].Start();
        }
    }

    public bool HasFreeSlot => _free.Count > 0;

    public int AliveThreads => _threads.Count(t => t.IsAlive);

    /// <summary>Main thread: takes a slot to fill and submit.</summary>
    public bool TryRentSlot([NotNullWhen(true)] out MeshJobSlot? slot)
    {
        if (_shutdown || _free.Count == 0)
        {
            slot = null;
            return false;
        }

        slot = _free.Pop();
        slot.Failure = null;
        return true;
    }

    /// <summary>Main thread: hands a filled slot to the workers.</summary>
    public void Submit(MeshJobSlot slot)
    {
        if (_shutdown)
        {
            _free.Push(slot);
            return;
        }

        _jobs.Add(slot);
    }

    /// <summary>Main thread: takes a finished slot, if any.</summary>
    public bool TryTakeResult([NotNullWhen(true)] out MeshJobSlot? slot)
    {
        if (_shutdown)
        {
            slot = null;
            return false;
        }

        return _results.TryDequeue(out slot);
    }

    /// <summary>Main thread: gives a slot back after its result was applied or dropped.</summary>
    public void ReturnSlot(MeshJobSlot slot)
    {
        slot.Prepared?.Dispose();
        slot.Prepared = null;
        _free.Push(slot);
    }

    /// <summary>Cancels the workers without waiting (scene teardown).</summary>
    public void Cancel()
    {
        lock (_lifecycleLock)
        {
            if (_shutdown)
            {
                return;
            }

            _shutdown = true;
            _jobs.CompleteAdding();
            _cancel.Cancel();
        }
    }

    /// <summary>Stops the workers and waits for them. After the timeout the results are detached: nothing reads them any more.</summary>
    /// <param name="timeout">Longest wait for the whole pool.</param>
    /// <returns>True when every worker finished in time.</returns>
    public bool Shutdown(TimeSpan timeout)
    {
        Cancel();
        var budget = new ShutdownBudget(_time, timeout);
        var joined = true;
        foreach (var thread in _threads)
        {
            joined &= thread.Join(budget.Remaining);
        }

        if (!joined)
        {
            LogShutdownTimeout(_threads.Count(t => t.IsAlive));
        }

        return joined;
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            Cancel();
            _disposeRequested = true;
            if (_remainingWorkers == 0)
            {
                Cleanup();
            }
        }
    }

    private void Cleanup()
    {
        if (!_slotsDisposed)
        {
            _slotsDisposed = true;
            CleanupCount++;
            foreach (var slot in _allSlots)
            {
                slot.Dispose();
            }

            _jobs.Dispose();
            _cancel.Dispose();
            _results.Clear();
            _free.Clear();
            _allSlots.Clear();
        }
    }

    private void WorkerLoop()
    {
        var mesher = new ChunkMesher();
        try
        {
            foreach (var slot in _jobs.GetConsumingEnumerable(_cancel.Token))
            {
                try
                {
                    _beforeBuild?.Invoke(slot);
                    mesher.Build(slot.Input, _table, slot.Output);
                    slot.Prepared = ChunkMeshUploader.Prepare(slot.Output, slot.WantsMesh, slot.WantsCollision);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // The failure is logged here and travels with the slot, so the main thread drops the result and the chunk keeps its old mesh.
                    slot.Failure = e;
                    LogMeshingFailed(e, slot.Job.Key.Construct.Value, slot.Job.Key.Chunk.X, slot.Job.Key.Chunk.Y, slot.Job.Key.Chunk.Z);
                }

                _results.Enqueue(slot);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop: Cancel() was called, nothing to report.
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding() ended the enumeration while a Take was pending: also a normal stop.
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _remainingWorkers--;
                if (_remainingWorkers == 0 && _disposeRequested)
                {
                    Cleanup();
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Meshing construct {Construct} chunk ({X}, {Y}, {Z}) failed on a worker thread; the result is dropped")]
    private partial void LogMeshingFailed(Exception exception, ulong construct, int x, int y, int z);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Alive} mesher thread(s) did not stop in time; their results are detached")]
    private partial void LogShutdownTimeout(int alive);
}
