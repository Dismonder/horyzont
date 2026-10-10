using Godot;

using Horyzont.Client;
using Horyzont.Client.Session;
using Horyzont.Client.Terrain;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;
using Horyzont.Net.Session;
using Horyzont.Persistence.Terrain;

using Microsoft.Extensions.Logging;

using GodotVector3I = Godot.Vector3I;
using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Terrain;

/// <summary>
/// The terrain replication of a remote client (K3-16): reports the cache and the viewers before the client says ready, takes the
/// manifests and blocks of the server, checks every block against the manifest and the envelope codec, keeps the verified blocks in the
/// persistent cache and pastes them into the own terrain, reports generator digests and lets the edit prediction put its overlay back.
/// The block source of the streams is the cache, so a block that is not loaded yet is served from it. Main thread only.
/// </summary>
public sealed class ClientTerrainReplicator : IClientSessionListener, IClientWorldReadiness, IDisposable
{
    /// <summary>Blocks decoded, stored and pasted per frame; the rest waits in order.</summary>
    public const int MaxAppliedPerFrame = 4;

    /// <summary>Frames a block that was stored but not loaded is watched for a stale stream read.</summary>
    public const int RecheckFrames = 600;

    private const int MaxRecheckBlocks = 256;
    private const int ChecksumRadiusBlocks = 4;

    private readonly ClientRuntime _runtime;
    private readonly OwnTerrainProvider _terrain;
    private readonly ITerrainViewerSource? _viewers;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TerrainViewReporter _reporter;
    private readonly TerrainChecksumScheduler _scheduler;
    private readonly Queue<AcceptedBlock> _accepted = new();
    private readonly Dictionary<TerrainBlockKey, WatchedBlock> _recheck = [];
    private readonly List<TerrainBlockKey> _recheckKeys = [];
    private readonly List<TerrainBlockKey> _candidates = [];
    private readonly List<TerrainBlockKey> _batch = [];
    private readonly List<TerrainBlockChecksum> _digests = [];
    private readonly List<TerrainBlockKey> _resend = [];
    private readonly VoxelBuffer _scratch = new();
    private ClientTerrainSyncState? _sync;
    private RemoteTerrainPrediction? _prediction;
    private long _frame;
    private long _blocksReceived;
    private long _bytesReceived;
    private long _pages;
    private long _stale;
    private long _rejected;
    private long _repairs;
    private long _checksums;

    /// <summary>Creates the replicator.</summary>
    /// <param name="runtime">The client runtime (its session sends the reports).</param>
    /// <param name="terrain">The own terrain of the client, with the cache as block source.</param>
    /// <param name="viewers">Where the viewers of the local player come from; null while there is no local player.</param>
    /// <param name="time">Clock of the report rates.</param>
    /// <param name="logger">Logger.</param>
    public ClientTerrainReplicator(
        ClientRuntime runtime, OwnTerrainProvider terrain, ITerrainViewerSource? viewers, TimeProvider time, ILogger<ClientTerrainReplicator> logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = runtime;
        _terrain = terrain;
        _viewers = viewers;
        _time = time;
        _logger = logger;
        _reporter = new TerrainViewReporter(time);
        _scheduler = new TerrainChecksumScheduler(time);
    }

    /// <summary>Gets the sync state of the session; null before the server hello.</summary>
    public ClientTerrainSyncState? Sync => _sync;

    /// <inheritdoc />
    public bool IsReady => _sync?.IsReady ?? false;

    /// <summary>Gets a snapshot of the counters for the interface.</summary>
    public TerrainReplicationStatistics Statistics => new(
        IsReady, _sync?.PendingCount ?? 0, _blocksReceived, _bytesReceived, _pages, _stale, _rejected, _repairs, _checksums,
        _prediction?.Count ?? 0, _terrain.Cache?.IsMemoryOnly ?? false);

    /// <inheritdoc />
    public void Dispose() => _scratch.Dispose();

    /// <summary>Connects the edit prediction that puts its overlay back over new authoritative blocks.</summary>
    /// <param name="prediction">The prediction.</param>
    public void Attach(RemoteTerrainPrediction prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        _prediction = prediction;
    }

    /// <summary>
    /// Starts the replication for a server hello: the terrain and the cache exist already. Sends the cache report and the first viewers
    /// <b>before</b> the caller says ready, so the server answers with the startup manifest.
    /// </summary>
    /// <param name="hello">The hello.</param>
    public void Prepare(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _accepted.Clear();
        _recheck.Clear();
        _reporter.Reset();
        _sync = new ClientTerrainSyncState(hello.Planets.Select(p => p.Id));
        _sync.BarrierCompleted += OnBarrierCompleted;
        _prediction?.StartSession(_sync, hello);
        var cache = _terrain.Cache;
        if (cache is not null)
        {
            ReportCache(hello, cache);
        }

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
    public void OnDisconnected(DisconnectCode code)
    {
        _accepted.Clear();
        _recheck.Clear();
        _prediction?.RevertAll();
    }

    /// <summary>One frame of the client: viewers, queued blocks, stale reads, checksums, edits waiting for their barrier. Main thread.</summary>
    public void OnFrame()
    {
        if (_sync is null || _runtime.Session.State is not (ClientState.Loading or ClientState.InGame))
        {
            return;
        }

        _frame++;
        SendView();
        ApplyAccepted();
        Recheck();
        SendChecksums();
        _prediction?.Expire(_resend, RequestBlocks);
        _terrain.StepDerived();
    }

    /// <summary>Asks the server for the current data of blocks it must send again (reported as missing).</summary>
    /// <param name="keys">LOD zero keys.</param>
    public void RequestBlocks(IReadOnlyCollection<TerrainBlockKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var group in keys.GroupBy(k => k.Planet))
        {
            foreach (var chunk in group.Chunk(ProtocolConstants.MaxTerrainCacheEntries))
            {
                _runtime.Session.Send(new TerrainCacheReport(group.Key, [.. chunk.Select(k => new TerrainBlockVersion(k, 0))]));
            }
        }
    }

    /// <summary>
    /// Pastes a block that passed the envelope check into a terrain whose block is loaded; the block is decoded into a private
    /// buffer first, so a bad envelope changes nothing. The destination is the block origin in voxels, also for negative coordinates.
    /// </summary>
    /// <param name="terrain">The voxel terrain.</param>
    /// <param name="data">The block message.</param>
    /// <param name="codec">The envelope codec.</param>
    /// <returns>True when the block was loaded and replaced; false when it is not loaded (the cache serves it on the next load).</returns>
    /// <exception cref="ProtocolViolationException">The envelope is bad.</exception>
    public static bool PasteValidated(VoxelLodTerrain terrain, TerrainBlockData data, TerrainBlockCodec codec)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(codec);
        using var buffer = new VoxelBuffer();
        codec.Decode(data.Data, buffer);
        return Paste(terrain.GetVoxelTool(), data.Key, buffer);
    }

    /// <summary>Pastes a decoded block when its area is loaded.</summary>
    /// <param name="tool">Voxel tool of the planet terrain.</param>
    /// <param name="key">LOD zero key.</param>
    /// <param name="block">The decoded 16³ block.</param>
    /// <returns>Whether the block was loaded.</returns>
    internal static bool Paste(VoxelTool tool, TerrainBlockKey key, VoxelBuffer block)
    {
        var origin = Origin(key);
        var size = new GodotVector3I(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
        if (!tool.IsAreaEditable(new Aabb(origin, size)))
        {
            return false;
        }

        tool.Paste(origin, block, TerrainChannels.UsedChannelsMask);
        return true;
    }

    private static GodotVector3I Origin(TerrainBlockKey key) =>
        new(key.X * TerrainMath.BlockSize, key.Y * TerrainMath.BlockSize, key.Z * TerrainMath.BlockSize);

    private void ReportCache(ServerHello hello, ClientTerrainCache cache)
    {
        var buffer = new TerrainBlockVersion[ProtocolConstants.MaxTerrainCacheEntries];
        var viewerPosition = _viewers is not null && _viewers.TryGetViewers(out var viewers) ? viewers.CollisionPosition : Vector3.Zero;
        foreach (var planet in hello.Planets)
        {
            var all = new List<TerrainBlockVersion>();
            int read;
            do
            {
                read = cache.CopyVersions(planet.Id, buffer, all.Count);
                all.AddRange(buffer.AsSpan(0, read));
            }
            while (read == buffer.Length);

            foreach (var report in TerrainCacheReports.Build(planet.Id, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(all), viewerPosition, planet.VoxelSizeMeters))
            {
                _runtime.Session.Send(report);
            }
        }
    }

    private void SendView()
    {
        if (_viewers is null || !_viewers.TryGetViewers(out var viewers) || !_reporter.TryCreate(viewers, out var view))
        {
            return;
        }

        _runtime.Session.Send(view);
    }

    private void OnSync(TerrainSync sync)
    {
        var state = _sync;
        if (state is null)
        {
            return;
        }

        _pages++;
        var progress = state.OnSync(sync);
        if (!progress.IsValid)
        {
            Leave($"manifest: {progress.Violation}");
            return;
        }

        if (sync.Purpose == 2 || sync.Entries.Count == 0)
        {
            return;
        }

        var cache = _terrain.Cache;
        _resend.Clear();
        foreach (var entry in sync.Entries)
        {
            var held = cache is not null && cache.TryGetHeld(entry.Key, out var version, out var hash)
                && version == entry.Version && hash == entry.Hash;
            if (held)
            {
                cache!.Expect(entry.Key, entry.Version, entry.Hash);
                continue;
            }

            if (!state.MarkPending(entry))
            {
                // A live block of this session is at least as new (it overtook the manifest): neither wait for the old version nor
                // let the expectation drop or hide the newer data.
                continue;
            }

            if (cache is null)
            {
                continue;
            }

            var sameVersion = cache.TryGetHeld(entry.Key, out var heldVersion, out _) && heldVersion == entry.Version;
            cache.Expect(entry.Key, entry.Version, entry.Hash);
            if (sameVersion)
            {
                // Equal version, other bytes (the server lost a partial save and edited again): it will not send what it thinks we hold.
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
        var state = _sync;
        if (state is null)
        {
            return;
        }

        _blocksReceived++;
        _bytesReceived += data.Data.Length;
        var hash = TerrainBlockHash.Compute(data.Data);
        var wasPending = state.IsPending(data.Key);
        switch (state.OnData(data.Key, data.Version, hash))
        {
            case TerrainDataVerdict.Stale:
                _stale++;
                return;
            case TerrainDataVerdict.Violation:
                _rejected++;
                Leave($"block {data.Key} contradicts its manifest");
                return;
            default:
                if (!wasPending && data.Version == 0)
                {
                    _repairs++;
                }

                _accepted.Enqueue(new AcceptedBlock(data.Key, data.Version, hash, data.Data));
                break;
        }
    }

    private void ApplyAccepted()
    {
        for (var i = 0; i < MaxAppliedPerFrame && _accepted.TryDequeue(out var block); i++)
        {
            try
            {
                _terrain.Codec.Decode(block.Envelope, _scratch);
            }
            catch (ProtocolViolationException exception)
            {
                // A bad block never reaches the cache or the scene; the good cache stays as it is.
                _rejected++;
                ClientTerrainLog.BadBlock(_logger, exception, block.Key);
                Leave($"block {block.Key} is not a valid envelope");
                return;
            }

            _terrain.Cache?.Put(block.Key, block.Version, block.Hash, block.Envelope);
            _terrain.OnBlockStored(block.Key);
            if (!_terrain.TryGetNode(block.Key.Planet, out var node))
            {
                continue;
            }

            if (_prediction is not null && _prediction.TryApplyAuthoritative(node, block.Key, _scratch))
            {
                continue;
            }

            if (!Paste(node.Tool, block.Key, _scratch))
            {
                Watch(block);
            }
        }
    }

    private void Watch(AcceptedBlock block)
    {
        if (_recheck.Count >= MaxRecheckBlocks && !_recheck.ContainsKey(block.Key))
        {
            return;
        }

        _recheck[block.Key] = new WatchedBlock(block.Envelope, _frame + RecheckFrames);
    }

    private void Recheck()
    {
        if (_recheck.Count == 0)
        {
            return;
        }

        // A stream read that began before the block was stored may deliver the older data to a block that loads now: paste again once.
        _recheckKeys.Clear();
        _recheckKeys.AddRange(_recheck.Keys);
        foreach (var key in _recheckKeys)
        {
            var watch = _recheck[key];
            if (!_terrain.TryGetNode(key.Planet, out var node) || watch.UntilFrame < _frame)
            {
                _recheck.Remove(key);
                continue;
            }

            if (!node.Tool.IsAreaEditable(new Aabb(Origin(key), new GodotVector3I(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize))))
            {
                continue;
            }

            _recheck.Remove(key);
            try
            {
                _terrain.Codec.Decode(watch.Envelope, _scratch);
            }
            catch (ProtocolViolationException)
            {
                continue;
            }

            if (_prediction is null || !_prediction.TryApplyAuthoritative(node, key, _scratch))
            {
                Paste(node.Tool, key, _scratch);
            }
        }
    }

    private void SendChecksums()
    {
        if (_runtime.Session.State != ClientState.InGame || _sync is not { IsReady: true } || _viewers is null || !_scheduler.IsDue
            || !_viewers.TryGetViewers(out var viewers) || !_terrain.TryGetNode(viewers.Planet, out var node))
        {
            return;
        }

        var cache = _terrain.Cache;
        var centre = new GodotVector3I(
            (int)MathF.Floor(viewers.CollisionPosition.X / TerrainMath.BlockSize),
            (int)MathF.Floor(viewers.CollisionPosition.Y / TerrainMath.BlockSize),
            (int)MathF.Floor(viewers.CollisionPosition.Z / TerrainMath.BlockSize));
        _candidates.Clear();
        for (var x = centre.X - ChecksumRadiusBlocks; x <= centre.X + ChecksumRadiusBlocks; x++)
        {
            for (var y = centre.Y - ChecksumRadiusBlocks; y <= centre.Y + ChecksumRadiusBlocks; y++)
            {
                for (var z = centre.Z - ChecksumRadiusBlocks; z <= centre.Z + ChecksumRadiusBlocks; z++)
                {
                    var key = new TerrainBlockKey(viewers.Planet, 0, x, y, z);
                    var block = new Aabb(Origin(key), new GodotVector3I(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize));

                    // Only blocks nobody edited: generated here, not held in the cache, not shaped by a prediction, not awaited.
                    if (!node.Tool.IsAreaEditable(block) || (cache is not null && cache.TryGetHeld(key, out _, out _))
                        || _sync.IsPending(key) || (_prediction?.Touches(key) ?? false))
                    {
                        continue;
                    }

                    _candidates.Add(key);
                }
            }
        }

        if (!_scheduler.TryTake(_candidates, _batch))
        {
            return;
        }

        _digests.Clear();
        foreach (var key in _batch)
        {
            node.Tool.Copy(Origin(key), _scratch, TerrainChannels.UsedChannelsMask);
            _digests.Add(new TerrainBlockChecksum(key, _terrain.Codec.Digest(_scratch)));
        }

        _checksums += _digests.Count;
        _runtime.Session.Send(new TerrainChecksums(viewers.Planet, [.. _digests]));
    }

    private void OnBarrierCompleted(OpId operation, IReadOnlyList<TerrainSyncEntry> entries) => _prediction?.OnBarrier(operation, entries);

    private void Leave(string reason)
    {
        ClientTerrainLog.Leaving(_logger, reason);
        _runtime.Session.Disconnect();
    }

    private readonly record struct AcceptedBlock(TerrainBlockKey Key, uint Version, ulong Hash, byte[] Envelope);

    private readonly record struct WatchedBlock(byte[] Envelope, long UntilFrame);
}
