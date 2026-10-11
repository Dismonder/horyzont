using System.Numerics;
using System.Runtime.InteropServices;

using Horyzont.Bots.Client;
using Horyzont.Bots.Digests;
using Horyzont.Client;
using Horyzont.Client.Replication;
using Horyzont.Client.Terrain;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Net.Session;
using Horyzont.Persistence.Terrain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Bots.Replicas;

/// <summary>
/// The terrain protocol of a remote client without an engine (K3-16), the minimal part of the game's <c>ClientTerrainReplicator</c>:
/// the cache report before ready, views from the feet of the bot, manifests and blocks checked by <see cref="ClientTerrainSyncState"/>,
/// verified blocks kept in a persistent <see cref="ClientTerrainCache"/> in the directory of the bot. Bots hold no voxels: the terrain of
/// a bot is the (key, version, envelope hash) of every block it holds. A contradiction of a manifest disconnects, as in the game.
/// </summary>
public sealed class TerrainReplica : IBotReplica, IDisposable
{
    /// <summary>Default view distance of a bot, in metres (visual and collision).</summary>
    public const uint DefaultViewDistance = 256;

    private const string Fingerprint = "k4-19-bot";

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TerrainViewReporter _reporter;
    private readonly List<TerrainBlockKey> _resend = [];
    private ClientSession? _session;
    private ClientTerrainSyncState? _sync;
    private IReadOnlyList<PlanetParams> _planets = [];
    private PlanetId _planet;
    private Vector3 _feet;
    private bool _hasFeet;

    /// <summary>Initializes a new instance of the <see cref="TerrainReplica"/> class.</summary>
    /// <param name="directory">Directory of the cache file of this bot (created when missing).</param>
    /// <param name="time">Clock of the view reports.</param>
    /// <param name="viewDistance">Visual and collision distance the bot declares, in metres.</param>
    /// <param name="loggers">Loggers; null for none.</param>
    public TerrainReplica(string directory, TimeProvider time, uint viewDistance = DefaultViewDistance, ILoggerFactory? loggers = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(time);
        Directory.CreateDirectory(directory);
        _time = time;
        var factory = loggers ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<TerrainReplica>();
        _reporter = new TerrainViewReporter(time);
        ViewDistance = viewDistance;

        // The cache only stamps usage with its clock and bounds its close; the system clock keeps CloseAsync from waiting on a virtual one.
        Cache = new ClientTerrainCache(Path.Combine(directory, "terrain.db"), Fingerprint, TimeProvider.System, factory.CreateLogger<ClientTerrainCache>());
    }

    /// <summary>Gets the persistent block cache of the bot.</summary>
    public ClientTerrainCache Cache { get; }

    /// <summary>Gets the view distance the bot declares, in metres.</summary>
    public uint ViewDistance { get; }

    /// <summary>Gets the sync state of the session; null before the first hello.</summary>
    public ClientTerrainSyncState? Sync => _sync;

    /// <summary>Gets the number of views sent.</summary>
    public long ViewsSent { get; private set; }

    /// <summary>Gets the number of cache reports sent.</summary>
    public long ReportsSent { get; private set; }

    /// <summary>Gets the number of blocks received.</summary>
    public long BlocksReceived { get; private set; }

    /// <summary>Gets the number of blocks accepted into the cache.</summary>
    public long BlocksStored { get; private set; }

    /// <summary>Gets the number of manifest pages received.</summary>
    public long SyncPages { get; private set; }

    /// <summary>Gets the number of stale blocks (an older version than known).</summary>
    public long StaleBlocks { get; private set; }

    /// <summary>Gets the number of protocol contradictions (each one disconnected the bot).</summary>
    public long Violations { get; private set; }

    /// <summary>Raised when the barrier of an own terrain edit completed: the operation and the blocks and versions it produced.</summary>
    public event Action<OpId, IReadOnlyList<TerrainSyncEntry>>? BarrierCompleted;

    /// <summary>Raised when a block was stored (key, version).</summary>
    public event Action<TerrainBlockKey, uint>? BlockStored;

    /// <inheritdoc />
    public ulong Digest => TerrainVersionDigest.Compute(Held().Select(e => new TerrainBlockVersion(e.Key, e.Version)));

    /// <inheritdoc />
    public bool IsReady => _sync?.IsReady ?? false;

    /// <inheritdoc />
    public ClientReplication? Replication => null;

    /// <inheritdoc />
    public void Attach(ClientSession session, IFullSnapshotSink snapshots)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <inheritdoc />
    public void Start(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _planets = hello.Planets;
        _reporter.Reset();
        _sync = new ClientTerrainSyncState(hello.Planets.Select(p => p.Id));
        _sync.BarrierCompleted += (operation, entries) => BarrierCompleted?.Invoke(operation, entries);
        Cache.ResetRefetch();
        ReportCache();
        SendView();
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        switch (message)
        {
            case TerrainSync sync:
                OnSync(sync);
                break;
            case TerrainBlockData data:
                OnBlock(data);
                break;
            default:
                break;
        }
    }

    /// <inheritdoc />
    public void OnFrame(PlanetId planet, Vector3 feet)
    {
        if (planet.Value != 0)
        {
            _planet = planet;
            _feet = feet;
            _hasFeet = true;
        }

        if (_sync is null || _session is null || _session.State is not (ClientState.Loading or ClientState.InGame))
        {
            return;
        }

        SendView();
        _resend.Clear();
        if (Cache.TakeRefetch(_resend) > 0)
        {
            RequestBlocks(_resend);
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => Cache.ResetRefetch();

    /// <inheritdoc />
    public void WriteDump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        DigestDump.Write(writer, Held().Select(e => TerrainVersionDigest.Entry(e.Key, e.Version, e.Hash)));
    }

    /// <summary>
    /// The blocks the bot holds near its feet: inside the view distance less one block diagonal (the edge of the view may lag a
    /// moving bot), the area in which the server keeps it current.
    /// </summary>
    /// <returns>Held blocks with version and envelope hash.</returns>
    public IReadOnlyList<(TerrainBlockKey Key, uint Version, ulong Hash)> Held()
    {
        var result = new List<(TerrainBlockKey, uint, ulong)>();
        if (!_hasFeet)
        {
            return result;
        }

        var voxel = _planets.FirstOrDefault(p => p.Id == _planet)?.VoxelSizeMeters ?? 1f;
        var edge = TerrainMath.BlockSize * voxel;
        var radius = ViewDistance - (edge * 1.75f);
        foreach (var (key, _) in AllVersions(_planet))
        {
            var center = new Vector3((key.X + 0.5f) * edge, (key.Y + 0.5f) * edge, (key.Z + 0.5f) * edge);
            if (key.Lod == 0 && Vector3.DistanceSquared(center, _feet) <= radius * radius
                && Cache.TryGetHeld(key, out var version, out var hash))
            {
                result.Add((key, version, hash));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose() => Cache.CloseAsync().GetAwaiter().GetResult();

    private List<TerrainBlockVersion> AllVersions(PlanetId planet)
    {
        var buffer = new TerrainBlockVersion[ProtocolConstants.MaxTerrainCacheEntries];
        var all = new List<TerrainBlockVersion>();
        int read;
        do
        {
            read = Cache.CopyVersions(planet, buffer, all.Count);
            all.AddRange(buffer.AsSpan(0, read));
        }
        while (read == buffer.Length);
        return all;
    }

    private void ReportCache()
    {
        foreach (var planet in _planets)
        {
            var all = AllVersions(planet.Id);
            var viewer = _hasFeet && _planet == planet.Id ? _feet : Vector3.Zero;
            foreach (var report in TerrainCacheReports.Build(planet.Id, CollectionsMarshal.AsSpan(all), viewer, planet.VoxelSizeMeters))
            {
                _session!.Send(report);
                ReportsSent++;
            }
        }
    }

    private void SendView()
    {
        if (!_hasFeet || _session is null)
        {
            return;
        }

        var viewers = new TerrainViewers(_planet, _feet, ViewDistance, _feet, ViewDistance);
        if (_reporter.TryCreate(viewers, out var view))
        {
            _session.Send(view);
            ViewsSent++;
        }
    }

    private void RequestBlocks(List<TerrainBlockKey> keys)
    {
        foreach (var group in keys.GroupBy(k => k.Planet))
        {
            foreach (var chunk in group.Chunk(ProtocolConstants.MaxTerrainCacheEntries))
            {
                _session!.Send(new TerrainCacheReport(group.Key, [.. chunk.Select(k => new TerrainBlockVersion(k, 0))]));
            }
        }
    }

    private void OnSync(TerrainSync sync)
    {
        if (_sync is not { } state)
        {
            return;
        }

        SyncPages++;
        var progress = state.OnSync(sync);
        if (!progress.IsValid)
        {
            Leave("manifest: " + progress.Violation);
            return;
        }

        if (sync.Purpose == 2 || sync.Entries.Count == 0)
        {
            return;
        }

        _resend.Clear();
        foreach (var entry in sync.Entries)
        {
            if (Cache.TryGetHeld(entry.Key, out var version, out var hash) && version == entry.Version && hash == entry.Hash)
            {
                Cache.Expect(entry.Key, entry.Version, entry.Hash);
                continue;
            }

            if (!state.MarkPending(entry, out var contradicts))
            {
                if (contradicts)
                {
                    Leave("manifest contradicts a received block");
                    return;
                }

                continue;
            }

            var sameVersion = Cache.TryGetHeld(entry.Key, out var heldVersion, out _) && heldVersion == entry.Version;
            Cache.Expect(entry.Key, entry.Version, entry.Hash);
            if (sameVersion)
            {
                _resend.Add(entry.Key);
            }
        }

        if (_resend.Count > 0)
        {
            RequestBlocks(_resend);
        }
    }

    private void OnBlock(TerrainBlockData data)
    {
        if (_sync is not { } state)
        {
            return;
        }

        BlocksReceived++;
        var hash = TerrainBlockHash.Compute(data.Data);
        switch (state.OnData(data.Key, data.Version, hash))
        {
            case TerrainDataVerdict.Stale:
                StaleBlocks++;
                return;
            case TerrainDataVerdict.Violation:
                Leave("block contradicts its manifest");
                return;
            default:
                Cache.Put(data.Key, data.Version, hash, data.Data);
                BlocksStored++;
                BlockStored?.Invoke(data.Key, data.Version);
                break;
        }
    }

    private void Leave(string reason)
    {
        Violations++;
        BotReplicaLog.TerrainViolation(_logger, reason);
        _session?.Disconnect();
    }
}

/// <summary>Log messages of the bot replicas.</summary>
internal static partial class BotReplicaLog
{
    [LoggerMessage(EventId = 4190, Level = LogLevel.Warning, Message = "Bot terrain replica leaves the server: {Reason}")]
    public static partial void TerrainViolation(ILogger logger, string reason);
}
