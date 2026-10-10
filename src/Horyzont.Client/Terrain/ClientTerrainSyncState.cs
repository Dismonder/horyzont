using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Terrain;

/// <summary>
/// Protocol bookkeeping of the terrain replication of a remote client, free of the engine: the manifest transfers per planet, the
/// blocks the manifests wait for, the monotonic versions of the session, the edit barriers, and the readiness of the world
/// (<see cref="IClientWorldReadiness"/>): true once the startup manifest of every planet ended and every block it asked for arrived.
/// One instance per session. Main thread.
/// </summary>
public sealed class ClientTerrainSyncState : IClientWorldReadiness
{
    private const byte PurposeStartup = 0;
    private const byte PurposeBarrier = 2;

    private readonly Dictionary<PlanetId, PlanetState> _planets = [];
    private readonly Dictionary<TerrainBlockKey, (uint Version, ulong Hash)> _pending = [];
    private readonly Dictionary<TerrainBlockKey, uint> _latest = [];
    private readonly Dictionary<OpId, IReadOnlyList<TerrainSyncEntry>> _barriers = [];
    private readonly Queue<OpId> _barrierOrder = new();
    private readonly int _maxBarriers;
    private int _startupOpen;
    private uint _lastTransfer;
    private bool _ready;

    /// <summary>Creates the state for the planets of the server hello.</summary>
    /// <param name="planets">The planets whose startup manifest is awaited.</param>
    /// <param name="maxBarriers">How many completed barriers are remembered for predictions that have not asked yet.</param>
    public ClientTerrainSyncState(IEnumerable<PlanetId> planets, int maxBarriers = 64)
    {
        ArgumentNullException.ThrowIfNull(planets);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBarriers, 1);
        foreach (var planet in planets)
        {
            if (_planets.TryAdd(planet, new PlanetState()))
            {
                _startupOpen++;
            }
        }

        _maxBarriers = maxBarriers;
    }

    /// <summary>Raised when an edit barrier ended: the final versions and hashes of the blocks of one accepted edit.</summary>
    public event Action<OpId, IReadOnlyList<TerrainSyncEntry>>? BarrierCompleted;

    /// <summary>
    /// Gets a value indicating whether the startup manifest of every planet ended and the blocks it asked for arrived. Once true it
    /// stays true for the session: later manifests do not freeze the player again.
    /// </summary>
    public bool IsReady
    {
        get
        {
            if (!_ready && _startupOpen == 0 && _pending.Count == 0)
            {
                _ready = true;
            }

            return _ready;
        }
    }

    /// <summary>Gets the number of blocks a manifest asked for that did not arrive yet.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Takes one page of a manifest or barrier transfer.</summary>
    /// <param name="sync">The page.</param>
    /// <returns>Its effect, or the violation of the protocol it is.</returns>
    public TerrainSyncProgress OnSync(TerrainSync sync)
    {
        ArgumentNullException.ThrowIfNull(sync);
        if (!_planets.TryGetValue(sync.Planet, out var planet))
        {
            return Fail(sync, "manifest of an unknown planet");
        }

        if (planet.Open is null)
        {
            if (sync.PageIndex != 0)
            {
                return Fail(sync, "a transfer does not start with page zero");
            }

            if (sync.TransferId <= _lastTransfer)
            {
                return Fail(sync, "transfer id not increasing");
            }

            if (sync.Purpose == PurposeStartup && planet.StartupDone)
            {
                return Fail(sync, "a second startup manifest for one planet");
            }

            _lastTransfer = sync.TransferId;
            planet.Open = new OpenTransfer(sync.TransferId, sync.Purpose, sync.BarrierOpId);
        }

        var open = planet.Open;
        if (sync.TransferId != open.Id || sync.Purpose != open.Purpose || sync.BarrierOpId != open.Barrier)
        {
            return Fail(sync, "pages of different transfers are interleaved");
        }

        if (sync.PageIndex != open.NextPage)
        {
            return Fail(sync, "page out of order");
        }

        if (sync.Entries.Count > 0)
        {
            if (open.LastKey is { } last && sync.Entries[0].Key <= last)
            {
                return Fail(sync, "keys not ascending across pages");
            }

            open.LastKey = sync.Entries[^1].Key;
            if (open.Purpose == PurposeBarrier)
            {
                open.BarrierEntries.AddRange(sync.Entries);
            }
        }

        open.NextPage++;
        if (!sync.IsLastPage)
        {
            return new TerrainSyncProgress(null, false, open.Purpose, open.Barrier);
        }

        planet.Open = null;
        if (open.Purpose == PurposeStartup)
        {
            planet.StartupDone = true;
            _startupOpen--;
        }
        else if (open.Purpose == PurposeBarrier)
        {
            CompleteBarrier(open.Barrier, open.BarrierEntries);
        }

        return new TerrainSyncProgress(null, true, open.Purpose, open.Barrier);
    }

    /// <summary>
    /// Notes that the manifest names a version of a block that the client does not hold; the block has to arrive. A block this session
    /// already received at the same or a newer version (a live edit overtook the manifest entry) is not awaited: the server would
    /// never send the older version again.
    /// </summary>
    /// <param name="entry">The manifest entry.</param>
    /// <returns>True when the block is awaited; false when a version at least this new was already seen.</returns>
    public bool MarkPending(TerrainSyncEntry entry)
    {
        if (_latest.TryGetValue(entry.Key, out var latest) && latest >= entry.Version)
        {
            return false;
        }

        _pending[entry.Key] = (entry.Version, entry.Hash);
        return true;
    }

    /// <summary>Whether a block is still awaited.</summary>
    /// <param name="key">The block.</param>
    /// <returns>True while pending.</returns>
    public bool IsPending(TerrainBlockKey key) => _pending.ContainsKey(key);

    /// <summary>Decides about a block the server sent.</summary>
    /// <param name="key">Block key.</param>
    /// <param name="version">Version of the data.</param>
    /// <param name="hash">Hash of the envelope as received.</param>
    /// <returns>What to do with it.</returns>
    public TerrainDataVerdict OnData(TerrainBlockKey key, uint version, ulong hash)
    {
        if (_pending.TryGetValue(key, out var wanted))
        {
            if (version < wanted.Version)
            {
                return TerrainDataVerdict.Stale;
            }

            if (version == wanted.Version && hash != wanted.Hash)
            {
                return TerrainDataVerdict.Violation;
            }

            // An equal block, or a newer one that an edit made after the manifest: either way the wait is over.
            _pending.Remove(key);
            _latest[key] = version;
            return TerrainDataVerdict.Accept;
        }

        if (_latest.TryGetValue(key, out var latest) && version < latest)
        {
            return TerrainDataVerdict.Stale;
        }

        _latest[key] = version;
        return TerrainDataVerdict.Accept;
    }

    /// <summary>Whether the final blocks of an edit barrier were seen.</summary>
    /// <param name="operation">The accepted operation.</param>
    /// <param name="entries">The final versions and hashes of its blocks.</param>
    /// <returns>True when the barrier has ended.</returns>
    public bool TryGetBarrier(OpId operation, out IReadOnlyList<TerrainSyncEntry> entries) => _barriers.TryGetValue(operation, out entries!);

    /// <summary>Forgets a barrier a prediction used.</summary>
    /// <param name="operation">The operation.</param>
    public void ForgetBarrier(OpId operation) => _barriers.Remove(operation);

    private static TerrainSyncProgress Fail(TerrainSync sync, string reason) =>
        new(reason, false, sync.Purpose, sync.BarrierOpId);

    private void CompleteBarrier(OpId operation, List<TerrainSyncEntry> entries)
    {
        IReadOnlyList<TerrainSyncEntry> final = entries.ToArray();
        _barriers[operation] = final;
        _barrierOrder.Enqueue(operation);
        while (_barrierOrder.Count > _maxBarriers)
        {
            _barriers.Remove(_barrierOrder.Dequeue());
        }

        BarrierCompleted?.Invoke(operation, final);
    }

    private sealed class PlanetState
    {
        public OpenTransfer? Open { get; set; }

        public bool StartupDone { get; set; }
    }

    private sealed class OpenTransfer(uint id, byte purpose, OpId barrier)
    {
        public uint Id { get; } = id;

        public byte Purpose { get; } = purpose;

        public OpId Barrier { get; } = barrier;

        public ushort NextPage { get; set; }

        public TerrainBlockKey? LastKey { get; set; }

        public List<TerrainSyncEntry> BarrierEntries { get; } = [];
    }
}
