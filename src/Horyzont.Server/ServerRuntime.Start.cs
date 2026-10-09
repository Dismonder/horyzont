using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Net.Session;
using Horyzont.Persistence;
using Horyzont.Persistence.Backup;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Writing;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server;

public sealed partial class ServerRuntime
{
    /// <summary>
    /// Creates or loads the world, replays its journal, writes the startup checkpoint and starts listening (spec: replay at start,
    /// steps 1 to 5; K2-10: start order). Expected problems with the data are returned as a <see cref="WorldStartResult"/>.
    /// </summary>
    /// <param name="ct">Cancellation of the start.</param>
    /// <returns>The outcome; when it is not running, the process exits with its <see cref="WorldStartResult.ExitCode"/>.</returns>
    /// <exception cref="InvalidOperationException">The runtime was started before.</exception>
    public async Task<WorldStartResult> StartAsync(CancellationToken ct)
    {
        if (RuntimeState != ServerRuntimeState.Created)
        {
            throw new InvalidOperationException("The server was started already.");
        }

        RuntimeState = ServerRuntimeState.Starting;
        WorldStartResult result;
        try
        {
            result = await StartCoreAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Faulted("the start failed", ex);
            await CleanUpAfterFailedStartAsync().ConfigureAwait(true);
            ExitCode = 70;
            RuntimeState = ServerRuntimeState.Faulted;
            throw;
        }

        if (!result.IsRunning)
        {
            ExitCode = result.ExitCode;
            RuntimeState = ServerRuntimeState.Stopped;
        }
        else if (_shutdownRequestedWhileStarting)
        {
            RequestShutdown();
        }

        return result;
    }

    private async Task<WorldStartResult> StartCoreAsync(CancellationToken ct)
    {
        var path = _options.WorldPath;
        var backups = _options.BackupsDirectory ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "backups");
        var persistence = _options.PersistenceOptions with { BackupsDirectory = backups };

        // 1. Create the file of a new world.
        if (!File.Exists(path))
        {
            var fresh = _createNewWorld();
            WorldCreator.Create(path, fresh, persistence, _time);
            _logger.WorldCreated(fresh.Id.Value, path);
        }

        // 2. Load: integrity, migrations, registries, checkpoint state and journal tail.
        LoadedWorld loaded;
        try
        {
            loaded = WorldLoader.Load(
                path,
                new WorldLoadOptions
                {
                    BackupsDirectory = backups,
                    BackupsToKeep = persistence.BackupsToKeep,
                    BlockTypeKeys = _options.BlockTypeKeys,
                    MaterialKeys = _options.MaterialKeys,
                },
                _time,
                _loggers.CreateLogger("Horyzont.Persistence.WorldLoader"));
        }
        catch (WorldCorruptedException ex)
        {
            _logger.WorldUnusable(ex.Message);
            return new WorldStartResult.Corrupted(ex.Message, ListBackups(backups, persistence));
        }
        catch (WorldFromNewerVersionException ex)
        {
            _logger.WorldUnusable(ex.Message);
            return new WorldStartResult.FromNewerVersion(ex.SchemaVersion, ex.SupportedVersion);
        }

        _logger.WorldLoaded(loaded.State.Id.Value, loaded.LastCheckpointOpId.Value, loaded.JournalTail.Count);

        // 3. Terrain edit semantics of the tail (K2-09, D1).
        var current = TerrainSemanticsMeta.Format(_terrainFactory.SemanticsVersion, _terrainFactory.VoxelRef);
        string saved;
        try
        {
            saved = TerrainSemanticsMeta.Read(path, _terrainFactory.VoxelRef);
        }
        catch (InvalidDataException ex)
        {
            _logger.WorldUnusable(ex.Message);
            return new WorldStartResult.Corrupted(ex.Message, ListBackups(backups, persistence));
        }

        var tailCheck = TerrainSemanticsMeta.CheckTail(saved, current, loaded.JournalTail);
        if (tailCheck.IsMismatch && _options.RecoverFromCheckpoint)
        {
            var (backup, dropped) = WorldBackups.DiscardJournalTail(path, loaded.LastCheckpointOpId, backups, persistence.BackupsToKeep, _time);
            _logger.DroppingTail(dropped, loaded.LastCheckpointOpId.Value, backup);
            loaded = loaded with { JournalTail = [] };
            tailCheck = TerrainSemanticsMeta.CheckTail(saved, current, loaded.JournalTail);
        }

        // The terrain never exists before the state is loaded (D7).
        _worldState = loaded.State;
        _terrain = _terrainFactory.Create(path, loaded.State);

        if (tailCheck.IsMismatch)
        {
            return StartReadOnly(tailCheck.Describe());
        }

        // 4. Writer and simulation.
        _writer = new PersistenceWriter(
            WorldDatabase.Open(path), persistence, _time, _options.Faults, _loggers.CreateLogger<PersistenceWriter>());
        IJournalSink sink = new JournalSink(_writer);
        if (_options.JournalSinkDecorator is { } decorate)
        {
            sink = decorate(sink);
        }

        _simulation = new ServerSimulation(loaded.State, _terrain.Service, sink, _options.Rules, _loggers.CreateLogger<ServerSimulation>());

        // 5. Replay (terrain areas are loaded before their edits).
        try
        {
            await _terrain.ReplayAsync(_simulation, loaded.JournalTail, ct).ConfigureAwait(true);
        }
        catch (ReplayFailedException ex)
        {
            _logger.ReplayFailed(ex.OpId.Value, ex.Message, ex);
            await CleanUpAfterFailedStartAsync().ConfigureAwait(true);
            return new WorldStartResult.ReplayFailed(ex.OpId, ex.Message);
        }
        catch (TimeoutException ex)
        {
            _logger.ReplayFailed(0, ex.Message, ex);
            await CleanUpAfterFailedStartAsync().ConfigureAwait(true);
            return new WorldStartResult.ReplayFailed(OpId.None, ex.Message);
        }

        // 6. Players that were online when the process died.
        var left = _simulation.LeaveAllOnlinePlayers();
        _logger.Replayed(loaded.JournalTail.Count, left.Count);

        // 7. The startup checkpoint, then the semantics string, before the first operation of the new run.
        try
        {
            var committed = await StartCheckpoint().WaitAsync(_options.StartupCheckpointTimeout, _time, ct).ConfigureAwait(true);
            PumpWriterEvents();
            if (!committed)
            {
                return await StartFailedAsync("the startup checkpoint was not written").ConfigureAwait(true);
            }
        }
        catch (TimeoutException)
        {
            return await StartFailedAsync("the startup checkpoint did not finish in time").ConfigureAwait(true);
        }

        TerrainSemanticsMeta.Write(path, current);
        _lastCheckpointAt = _time.GetTimestamp();

        // 8. The startup backup; a failure is only logged (E1).
        if (!await _writer.BackupAsync(ServerBackupReasons.Startup).ConfigureAwait(true))
        {
            _logger.StartupBackupFailed();
        }

        // 9. Sessions.
        StartSession();
        RuntimeState = ServerRuntimeState.Running;
        _logger.Running(loaded.State.Id.Value, RuntimeState);
        return new WorldStartResult.Started();
    }

    private WorldStartResult.StartedReadOnly StartReadOnly(string message)
    {
        _readOnlyStart = true;
        _logger.StartedReadOnly(message);
        _simulation = new ServerSimulation(_worldState!, _terrain!.Service, new RefusingJournalSink(), _options.Rules, _loggers.CreateLogger<ServerSimulation>());
        StartSession();
        RuntimeState = ServerRuntimeState.SafeMode;
        _logger.Running(_worldState!.Id.Value, RuntimeState);
        return new WorldStartResult.StartedReadOnly(ReadOnlyReason.TerrainSemanticsMismatch, message);
    }

    private void StartSession()
    {
        var hooks = new GameServerHooks(_simulation!, () => RuntimeState == ServerRuntimeState.Running, _loggers.CreateLogger<GameServerHooks>());
        _session = new ServerSession(_transport, _options.ServerOptions, hooks, _time, _loggers.CreateLogger<ServerSession>());
        foreach (var endpoint in _options.Listen)
        {
            _transport.Listen(endpoint);
        }
    }

    private IReadOnlyList<string> ListBackups(string backups, PersistenceOptions persistence) =>
        new BackupService(backups, Math.Max(1, persistence.BackupsToKeep), _time).List();

    private async Task<WorldStartResult> StartFailedAsync(string message)
    {
        _logger.StartupCheckpointFailed(message);
        await CleanUpAfterFailedStartAsync().ConfigureAwait(true);
        return new WorldStartResult.StartFailed(message);
    }

    private async Task CleanUpAfterFailedStartAsync()
    {
        if (_writer is not null)
        {
            try
            {
                await _writer.DisposeAsync().ConfigureAwait(true);
            }
#pragma warning disable CA1031 // Cleaning up after a failed start: report the original failure, not this one.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.ShutdownStepFailed("start cleanup: dispose writer", ex);
            }

            _writer = null;
        }

        if (_terrain is not null)
        {
            try
            {
                await _terrain.ShutdownAsync().ConfigureAwait(true);
            }
#pragma warning disable CA1031 // Cleaning up after a failed start: report the original failure, not this one.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.ShutdownStepFailed("start cleanup: shut terrain down", ex);
            }
        }
    }

    /// <summary>The sink of a world that must not change: any append is a bug.</summary>
    private sealed class RefusingJournalSink : IJournalSink
    {
        public void Append(JournalEntry entry) =>
            throw new InvalidOperationException("The server is read-only; no operation may be journaled.");
    }
}
