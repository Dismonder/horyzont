using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Server.Replication;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Terrain;

/// <summary>
/// Replicates the edited terrain of the server to remote clients (K3-16): the manifest of versions and hashes in answer to the cache
/// report of a client, the data of edits as they happen, the interest of the viewers, the barrier that ends the prediction of the
/// author of an edit, and the repair of generator blocks. The local player of a host gets nothing: it shares the terrain. Everything
/// runs on the main thread; all traffic goes through the reliable budget shared with entity replication.
/// </summary>
public sealed class ServerTerrainReplicator : IServerClientMessageHandler, IServerPlayerObserver, IServerTickParticipant
{
    private const byte PurposeStartup = 0;
    private const byte PurposeInterest = 1;
    private const byte PurposeBarrier = 2;
    private const int BulkClass = 1;
    private const int LiveClass = 0;
    private const int MaxResendKeys = 4096;

    private readonly ITerrainReplicationHost _host;
    private readonly ITerrainReplicationSource _source;
    private readonly WorldState _state;
    private readonly IReplicationSendBudget _budget;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly string _build;
    private readonly TerrainSendQueue _queue;
    private readonly Dictionary<ulong, PlayerTerrainState> _players = [];
    private readonly List<PlayerTerrainState> _ordered = [];
    private readonly ByteWriter _writer = new();
    private int _exportsLeft;
    private int _checksLeft;

    /// <summary>Creates the replicator.</summary>
    /// <param name="host">The running server.</param>
    /// <param name="source">Block data of the server terrain.</param>
    /// <param name="state">World state: planets and block versions.</param>
    /// <param name="budget">The reliable replication budget shared with entity replication.</param>
    /// <param name="time">Clock of the aging of tasks and of the checksum window.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="buildHash">Build label written to the log with a generator mismatch.</param>
    public ServerTerrainReplicator(
        ITerrainReplicationHost host, ITerrainReplicationSource source, WorldState state, IReplicationSendBudget budget, TimeProvider time,
        ILogger logger, string buildHash = "")
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _host = host;
        _source = source;
        _state = state;
        _budget = budget;
        _time = time;
        _logger = logger;
        _build = buildHash;
        _queue = new TerrainSendQueue(budget, time.TimestampFrequency);
    }

    /// <summary>Gets the number of generator blocks a client computed differently from the server (all clients, since start).</summary>
    public long GeneratorMismatches { get; private set; }

    /// <summary>Gets the number of block data messages sent.</summary>
    public long BlocksSent { get; private set; }

    /// <summary>Gets the encoded bytes of the block data messages sent.</summary>
    public long BlockBytesSent { get; private set; }

    /// <summary>Gets the number of manifest pages sent.</summary>
    public long SyncPagesSent { get; private set; }

    /// <summary>Gets the encoded bytes of the manifest pages sent.</summary>
    public long SyncBytesSent { get; private set; }

    /// <summary>Gets the number of players that currently receive terrain data.</summary>
    public int ReadyPlayers
    {
        get
        {
            var count = 0;
            for (var i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i].Ready)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets the number of block tasks waiting for a player.</summary>
    /// <param name="player">The player.</param>
    /// <returns>Waiting distinct keys.</returns>
    public int QueuedBlocks(PlayerId player) => _queue.Count(player);

    /// <summary>Gets the number of cached keys a client reported for a planet and the startup transfer did not use yet (tests).</summary>
    /// <param name="player">The player.</param>
    /// <param name="planet">The planet.</param>
    /// <returns>Number of distinct reported keys.</returns>
    internal int ReportedKeys(PlayerId player, PlanetId planet) =>
        _players.TryGetValue(player.Value, out var state) && state.Reports.TryGetValue(planet, out var set) ? set.Count : 0;

    /// <inheritdoc />
    public bool Handle(PlayerId player, Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is not (TerrainCacheReport or TerrainChecksums or TerrainView))
        {
            return false;
        }

        if (_host.IsLocalPlayer(player))
        {
            // The local player of a host shares the terrain of the server; it has nothing to report and gets nothing.
            return true;
        }

        var state = StateOf(player);
        switch (message)
        {
            case TerrainCacheReport report:
                HandleReport(state, report);
                break;
            case TerrainChecksums checksums:
                HandleChecksums(state, checksums);
                break;
            case TerrainView view:
                HandleView(state, view);
                break;
            default:
                break;
        }

        return true;
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        if (_host.IsLocalPlayer(player))
        {
            return;
        }

        var state = StateOf(player);
        state.Ready = true;
        foreach (var planet in _state.Planets.All)
        {
            var candidates = new SortedDictionary<TerrainBlockKey, uint>();
            if (state.Reports.TryGetValue(planet.Id, out var reported))
            {
                foreach (var pair in reported)
                {
                    candidates[pair.Key] = pair.Value;
                }
            }

            if (state.View is { } view && view.Planet == planet.Id)
            {
                AddEditedInRange(candidates, view, planet);
            }

            QueueTransfer(state, new TerrainTransferJob
            {
                Purpose = PurposeStartup,
                Planet = planet.Id,
                Candidates = [.. candidates],
            });
        }

        state.Reports.Clear();
        state.InterestView = state.View;
        state.InterestDirty = false;
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
    {
        if (_players.Remove(player.Value, out var state))
        {
            _ordered.Remove(state);
        }

        _queue.Remove(player);
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime) => Step();

    /// <inheritdoc />
    public void OnShutdown(ServerRuntime runtime)
    {
        _players.Clear();
        _ordered.Clear();
        _queue.Clear();
    }

    /// <summary>Takes a terrain edit that was just applied: queues the changed blocks for the clients that can see them.</summary>
    /// <param name="applied">The applied operation (subscribe to <c>ServerSimulation.Applied</c>).</param>
    public void OnOperationApplied(AppliedOperation applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        if (!OperationTerrain.TryGetTerrainEdit(applied.Operation, out var edit) || applied.TerrainVersions.Count == 0 || !_state.Planets.TryGet(edit.Planet, out var planet))
        {
            return;
        }

        var required = new SortedDictionary<TerrainBlockKey, uint>();
        foreach (var changed in applied.TerrainVersions)
        {
            required[changed.Key] = changed.Version;
            if (!_source.TryExportBlock(changed.Key, out var version, out var blob))
            {
                _logger.ExportUnavailable(changed.Key);
                continue;
            }

            var center = BlockCenter(changed.Key, planet.VoxelSizeMeters);
            foreach (var player in _ordered)
            {
                if (!player.Ready)
                {
                    continue;
                }

                var author = applied.Actor == player.Player;
                var distanceSquared = 0f;
                var near = false;
                var covered = player.View is { } view && view.Planet == edit.Planet && view.Covers(center, out distanceSquared, out near);
                if (covered || author)
                {
                    Enqueue(player, changed.Key, version, blob, author || near ? LiveClass : BulkClass, distanceSquared);
                }
            }
        }

        if (applied.Actor is { } actor && _players.TryGetValue(actor.Value, out var authorState) && authorState.Ready)
        {
            authorState.Barriers.Add(new TerrainBarrier(applied.Id, edit.Planet, required));
        }
    }

    /// <summary>One step of the replication of every ready client; called once per server tick.</summary>
    internal void Step()
    {
        _exportsLeft = TerrainReplicationPolicy.MaxExportsPerTick;
        _checksLeft = TerrainReplicationPolicy.MaxChecksumChecksPerTick;
        var now = _time.GetTimestamp();
        for (var i = 0; i < _ordered.Count; i++)
        {
            var player = _ordered[i];
            if (!player.Ready)
            {
                continue;
            }

            ProcessResend(player);
            ProcessChecksums(player);
            StartInterestTransfer(player);
            AdvanceTransfers(player);
            SendBlocks(player, now);
            ProcessBarriers(player);
            AdvanceTransfers(player);
        }
    }

    private static Vector3 BlockCenter(TerrainBlockKey key, float voxelSize)
    {
        var edge = TerrainMath.BlockSize * voxelSize;
        return new Vector3((key.X + 0.5f) * edge, (key.Y + 0.5f) * edge, (key.Z + 0.5f) * edge);
    }

    private PlayerTerrainState StateOf(PlayerId player)
    {
        if (!_players.TryGetValue(player.Value, out var state))
        {
            state = new PlayerTerrainState(player);
            _players.Add(player.Value, state);

            // Players are served in the order of their ids so the work of a tick does not depend on hash order.
            var index = _ordered.FindIndex(p => p.Player.Value > player.Value);
            if (index < 0)
            {
                _ordered.Add(state);
            }
            else
            {
                _ordered.Insert(index, state);
            }
        }

        return state;
    }

    private void HandleReport(PlayerTerrainState player, TerrainCacheReport report)
    {
        if (!_state.Planets.TryGet(report.Planet, out _))
        {
            return;
        }

        if (player.Ready)
        {
            // After the startup transfer a report is a request: the client could not accept the block it holds (its hash differs from
            // the manifest at an equal version) and reports it as missing.
            foreach (var entry in report.Entries)
            {
                if (entry.Key.Lod == 0 && entry.Key.Planet == report.Planet && entry.Version != _state.TerrainVersions.Get(entry.Key)
                    && player.ResendWork.Count < MaxResendKeys)
                {
                    player.ResendWork.Enqueue(entry.Key);
                }
            }

            return;
        }

        if (!player.Reports.TryGetValue(report.Planet, out var set))
        {
            set = [];
            player.Reports.Add(report.Planet, set);
        }

        foreach (var entry in report.Entries)
        {
            if (entry.Key.Lod != 0 || entry.Key.Planet != report.Planet || entry.Version == 0)
            {
                continue;
            }

            if (set.Count >= TerrainReplicationPolicy.MaxReportKeysPerPlanet && !set.ContainsKey(entry.Key))
            {
                _logger.ReportTruncated(player.Player.Value, report.Planet.Value, TerrainReplicationPolicy.MaxReportKeysPerPlanet);
                return;
            }

            set[entry.Key] = entry.Version;
        }
    }

    private void HandleView(PlayerTerrainState player, TerrainView view)
    {
        if (!_state.Planets.TryGet(view.Planet, out _))
        {
            RejectView(player, "unknown planet");
            return;
        }

        if (view.ViewSeq <= player.LastViewSeq)
        {
            RejectView(player, "sequence not increasing");
            return;
        }

        if (!float.IsFinite(view.VisualDistance) || !float.IsFinite(view.CollisionDistance) || view.VisualDistance < 0 || view.CollisionDistance < 0
            || view.VisualDistance > TerrainReplicationPolicy.MaxViewDistance || view.CollisionDistance > TerrainReplicationPolicy.MaxViewDistance
            || !IsFinite(view.VisualPosition) || !IsFinite(view.CollisionPosition))
        {
            RejectView(player, "out of range values");
            return;
        }

        switch (_host.GetFeet(player.Player, view.Planet, out var feet, out var extraTolerance))
        {
            case TerrainFeet.TooFar:
                RejectView(player, "too far from the planet");
                return;
            case TerrainFeet.Found:
                if (Vector3.Distance(view.CollisionPosition, feet) > TerrainReplicationPolicy.ViewerTolerance + extraTolerance)
                {
                    RejectView(player, "collision viewer far from the character");
                    return;
                }

                if (!_host.FreeCameraAllowed && Vector3.Distance(view.VisualPosition, feet) > TerrainReplicationPolicy.ViewerTolerance)
                {
                    RejectView(player, "visual viewer far from the character");
                    return;
                }

                break;
        }

        // Without a character (none spawned yet, or a server without characters) the position cannot be checked; the distances and
        // the sequence still are, and nothing is loaded for the declared sphere.
        var accepted = new TerrainViewState(view.Planet, view.VisualPosition, view.VisualDistance, view.CollisionPosition, view.CollisionDistance);
        player.LastViewSeq = view.ViewSeq;
        var differs = player.InterestView is not { } before || accepted.InterestDiffers(before);
        player.View = accepted;
        if (player.Ready && differs)
        {
            player.InterestDirty = true;
        }
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private void RejectView(PlayerTerrainState player, string reason)
    {
        // A misbehaving or lagging client must not flood the log: one entry per second and player.
        var now = _time.GetTimestamp();
        if (player.HadViolationLog && _time.GetElapsedTime(player.LastViolationLogAt, now) < TimeSpan.FromSeconds(1))
        {
            return;
        }

        player.HadViolationLog = true;
        player.LastViolationLogAt = now;
        _logger.ViewRejected(player.Player.Value, reason);
    }

    private void HandleChecksums(PlayerTerrainState player, TerrainChecksums checksums)
    {
        if (!player.Ready || player.View is not { } view || view.Planet != checksums.Planet)
        {
            _logger.ChecksumIgnored(player.Player.Value, "not ready or another planet than the viewers");
            return;
        }

        var now = _time.GetTimestamp();
        if (player.HadChecksum && _time.GetElapsedTime(player.LastChecksumAt, now) < TerrainReplicationPolicy.ChecksumWindow)
        {
            _logger.ChecksumIgnored(player.Player.Value, "too frequent");
            return;
        }

        player.HadChecksum = true;
        player.LastChecksumAt = now;
        _state.Planets.TryGet(checksums.Planet, out var planet);
        var examined = Math.Min(checksums.Entries.Count, TerrainReplicationPolicy.MaxChecksumEntries);
        for (var i = 0; i < examined; i++)
        {
            var entry = checksums.Entries[i];
            if (entry.Key.Lod != 0 || entry.Key.Planet != checksums.Planet || planet is null)
            {
                continue;
            }

            // The server never computes generator blocks far from the character of the client: no oracle of the whole planet.
            var centre = BlockCenter(entry.Key, planet.VoxelSizeMeters);
            if (Vector3.Distance(centre, view.CollisionPosition) <= TerrainReplicationPolicy.ChecksumRadius)
            {
                player.ChecksumWork.Enqueue(new KeyValuePair<TerrainBlockKey, ulong>(entry.Key, entry.Hash));
            }
        }
    }

    private void ProcessResend(PlayerTerrainState player)
    {
        while (_exportsLeft > 0 && player.ResendWork.TryDequeue(out var key))
        {
            _exportsLeft--;
            if (!_source.TryExportBlock(key, out var version, out var blob))
            {
                _logger.ExportUnavailable(key);
                continue;
            }

            player.Known.Remove(key);
            Enqueue(player, key, version, blob, BulkClass, DistanceSquaredTo(player, key));
        }
    }

    private void ProcessChecksums(PlayerTerrainState player)
    {
        while (player.ChecksumWork.Count > 0 && _checksLeft > 0 && _exportsLeft > 0)
        {
            var work = player.ChecksumWork.Dequeue();
            var key = work.Key;
            var serverVersion = _state.TerrainVersions.Get(key);
            if (serverVersion > 0)
            {
                // The block was edited: the checksum of the generator is not comparable. Make sure the client holds the current data.
                if (!player.Known.TryGetValue(key, out var held) || held.Version != serverVersion)
                {
                    SendCurrent(player, key);
                }

                continue;
            }

            _checksLeft--;
            if (!_source.TryGetGeneratedDigest(key, out var digest) || digest == work.Value)
            {
                continue;
            }

            GeneratorMismatches++;
            _logger.GeneratorMismatch(player.Player.Value, key, work.Value, _build, GeneratorMismatches);
            SendCurrent(player, key);
        }
    }

    private void SendCurrent(PlayerTerrainState player, TerrainBlockKey key)
    {
        _exportsLeft--;
        if (!_source.TryExportBlock(key, out var version, out var blob))
        {
            _logger.ExportUnavailable(key);
            return;
        }

        player.Known.Remove(key);
        Enqueue(player, key, version, blob, BulkClass, DistanceSquaredTo(player, key));
    }

    private void StartInterestTransfer(PlayerTerrainState player)
    {
        if (!player.InterestDirty || player.View is not { } view || !_state.Planets.TryGet(view.Planet, out var planet))
        {
            return;
        }

        if (player.Transfers.TryGetValue(view.Planet, out var transfers) && transfers.Pending.Exists(static j => j.Purpose == PurposeInterest))
        {
            return;
        }

        player.InterestDirty = false;
        player.InterestView = view;
        var candidates = new SortedDictionary<TerrainBlockKey, uint>();
        AddEditedInRange(candidates, view, planet);
        QueueTransfer(player, new TerrainTransferJob { Purpose = PurposeInterest, Planet = view.Planet, Candidates = [.. candidates] });
    }

    private void AddEditedInRange(SortedDictionary<TerrainBlockKey, uint> candidates, TerrainViewState view, PlanetParams planet)
    {
        foreach (var edited in _state.TerrainVersions.All)
        {
            if (edited.Key.Planet == planet.Id && edited.Key.Lod == 0 && view.Covers(BlockCenter(edited.Key, planet.VoxelSizeMeters), out _, out _)
                && !candidates.ContainsKey(edited.Key))
            {
                candidates.Add(edited.Key, 0);
            }
        }
    }

    private static void QueueTransfer(PlayerTerrainState player, TerrainTransferJob job)
    {
        if (!player.Transfers.TryGetValue(job.Planet, out var transfers))
        {
            transfers = new PlanetTransfers();
            player.Transfers.Add(job.Planet, transfers);
        }

        if (job.Purpose == PurposeBarrier)
        {
            transfers.Pending.Insert(0, job);
        }
        else
        {
            transfers.Pending.Add(job);
        }
    }

    private void AdvanceTransfers(PlayerTerrainState player)
    {
        var planets = _state.Planets.All;
        for (var i = 0; i < planets.Count; i++)
        {
            var planet = planets[i];
            if (!player.Transfers.TryGetValue(planet.Id, out var transfers))
            {
                continue;
            }

            if (transfers.Active is { Purpose: < 2, TransferId: 0 } waiting && transfers.Pending.Count > 0 && transfers.Pending[0].Purpose == PurposeBarrier)
            {
                // A barrier closes a live edit and must not wait for a bulk manifest that has not started sending: that one resumes
                // right after it (its build state is kept in the job).
                transfers.Pending.Insert(1, waiting);
                transfers.Active = null;
            }

            if (transfers.Active is null)
            {
                if (transfers.Pending.Count == 0)
                {
                    continue;
                }

                transfers.Active = transfers.Pending[0];
                transfers.Pending.RemoveAt(0);
            }

            var job = transfers.Active;
            if (!job.IsBuilt)
            {
                Build(player, job, planet);
                if (!job.IsBuilt)
                {
                    continue;
                }
            }

            if (job.Purpose == PurposeInterest && job.Entries.Count == 0)
            {
                // Nothing the client lacks: no transfer at all.
                transfers.Active = null;
                continue;
            }

            if (SendPages(player, job))
            {
                transfers.Active = null;
            }
        }
    }

    private void Build(PlayerTerrainState player, TerrainTransferJob job, PlanetParams planet)
    {
        while (job.NextCandidate < job.Candidates.Count)
        {
            var (key, clientVersion) = (job.Candidates[job.NextCandidate].Key, job.Candidates[job.NextCandidate].Value);
            var serverVersion = _state.TerrainVersions.Get(key);
            if ((serverVersion == 0 && clientVersion == 0)
                || (job.Purpose == PurposeInterest && player.Known.TryGetValue(key, out var held) && held.Version == serverVersion))
            {
                job.NextCandidate++;
                continue;
            }

            if (_exportsLeft <= 0)
            {
                return;
            }

            _exportsLeft--;
            job.NextCandidate++;
            if (!_source.TryExportBlock(key, out var version, out var blob))
            {
                _logger.ExportUnavailable(key);
                continue;
            }

            var hash = TerrainBlockHash.Compute(blob);
            job.Entries.Add(new TerrainSyncEntry(key, version, hash));
            if (job.Purpose == PurposeStartup && clientVersion == version)
            {
                // The client holds this version; it checks the hash itself and asks again when it differs.
                player.Known[key] = (version, hash);
            }
            else
            {
                Enqueue(player, key, version, blob, BulkClass, DistanceSquaredTo(player, key));
            }
        }
    }

    private bool SendPages(PlayerTerrainState player, TerrainTransferJob job)
    {
        if (job.TransferId == 0)
        {
            job.TransferId = ++player.LastTransferId;
        }

        var traffic = job.Purpose == PurposeBarrier ? ReplicationTraffic.Live : ReplicationTraffic.Bulk;
        while (!job.FinalSent)
        {
            var remaining = job.Entries.Count - job.NextEntry;
            var count = Math.Min(remaining, ProtocolConstants.MaxTerrainSyncEntries);
            var last = count == 0;
            var message = new TerrainSync(
                job.TransferId, job.Purpose, job.Planet, job.Barrier, job.NextPage, last,
                last ? [] : job.Entries.GetRange(job.NextEntry, count));
            _writer.Reset();
            MessageCodec.Write(_writer, message);
            var bytes = _writer.Length;
            if (!_budget.TryConsume(player.Player, traffic, bytes))
            {
                return false;
            }

            _host.Send(player.Player, message);
            SyncPagesSent++;
            SyncBytesSent += bytes;
            if (last)
            {
                job.FinalSent = true;
            }
            else
            {
                job.NextEntry += count;
                job.NextPage++;
            }
        }

        return true;
    }

    private void SendBlocks(PlayerTerrainState player, long now)
    {
        // Bulk data waits for the manifest that describes it; live edits do not wait behind a bulk manifest.
        var liveOnly = player.ManifestPending;
        for (var i = 0; i < TerrainReplicationPolicy.MaxBlocksPerFrame; i++)
        {
            if (!_queue.TryTake(player.Player, now, liveOnly, out var data) || data is null)
            {
                break;
            }

            _host.Send(player.Player, data);
            BlocksSent++;
            BlockBytesSent += _queue.LastTakenBytes;
            player.Known[data.Key] = (data.Version, TerrainBlockHash.Compute(data.Data));
        }

        if (_queue.NeedsResync(player.Player))
        {
            // The queue dropped the farthest tasks. What the client lacks is found again from the versions it is known to hold.
            _queue.ResetResync(player.Player);
            player.InterestDirty = true;
            player.InterestView = null;
            _logger.QueueOverflow(player.Player.Value);
        }
    }

    private static void ProcessBarriers(PlayerTerrainState player)
    {
        for (var i = player.Barriers.Count - 1; i >= 0; i--)
        {
            var barrier = player.Barriers[i];
            var complete = true;
            foreach (var pair in barrier.Required)
            {
                if (!player.Known.TryGetValue(pair.Key, out var held) || held.Version < pair.Value)
                {
                    complete = false;
                    break;
                }
            }

            if (!complete)
            {
                continue;
            }

            var job = new TerrainTransferJob { Purpose = PurposeBarrier, Planet = barrier.Planet, Barrier = barrier.Operation };
            foreach (var pair in barrier.Required)
            {
                var held = player.Known[pair.Key];
                job.Entries.Add(new TerrainSyncEntry(pair.Key, held.Version, held.Hash));
            }

            player.Barriers.RemoveAt(i);
            QueueTransfer(player, job);
        }
    }

    private void Enqueue(PlayerTerrainState player, TerrainBlockKey key, uint version, byte[] blob, int priorityClass, float distanceSquared)
    {
        if (blob.Length > ProtocolConstants.MaxTerrainBlockDataBytes)
        {
            // The codec refuses such a block and it is never split into another format; the client keeps its block.
            _logger.ExportUnavailable(key);
            return;
        }

        _queue.Enqueue(player.Player, key, version, blob, priorityClass, distanceSquared);
    }

    private float DistanceSquaredTo(PlayerTerrainState player, TerrainBlockKey key)
    {
        if (player.View is not { } view || !_state.Planets.TryGet(key.Planet, out var planet))
        {
            return 0f;
        }

        view.Covers(BlockCenter(key, planet.VoxelSizeMeters), out var distanceSquared, out _);
        return distanceSquared;
    }
}
