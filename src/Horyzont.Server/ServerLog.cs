using Horyzont.Core.Protocol;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server;

/// <summary>Log entries of the server runtime, hooks and console; player entries name the player, operation entries the operation.</summary>
internal static partial class ServerLog
{
    [LoggerMessage(EventId = 8001, Level = LogLevel.Information, Message = "Created world {WorldId} in {Path}")]
    public static partial void WorldCreated(this ILogger logger, Guid worldId, string path);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Information, Message = "Loaded world {WorldId}: last checkpoint operation {CheckpointOp}, {TailCount} journal entries to replay")]
    public static partial void WorldLoaded(this ILogger logger, Guid worldId, ulong checkpointOp, int tailCount);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Error, Message = "The world cannot be opened: {Message}")]
    public static partial void WorldUnusable(this ILogger logger, string message);

    [LoggerMessage(EventId = 8004, Level = LogLevel.Error, Message = "SAFE MODE, nothing is written: {Message}")]
    public static partial void StartedReadOnly(this ILogger logger, string message);

    [LoggerMessage(EventId = 8005, Level = LogLevel.Warning, Message = "Dropping {TailCount} journal entries after operation {CheckpointOp} (recover from checkpoint); backup {Backup}")]
    public static partial void DroppingTail(this ILogger logger, int tailCount, ulong checkpointOp, string backup);

    [LoggerMessage(EventId = 8006, Level = LogLevel.Error, Message = "Replay failed at operation {Operation}: {Message}")]
    public static partial void ReplayFailed(this ILogger logger, ulong operation, string message, Exception exception);

    [LoggerMessage(EventId = 8007, Level = LogLevel.Information, Message = "Replayed {Count} journal entries; {Left} players were left online")]
    public static partial void Replayed(this ILogger logger, int count, int left);

    [LoggerMessage(EventId = 8008, Level = LogLevel.Error, Message = "The startup checkpoint failed: {Message}")]
    public static partial void StartupCheckpointFailed(this ILogger logger, string message);

    [LoggerMessage(EventId = 8009, Level = LogLevel.Warning, Message = "The startup backup was not made")]
    public static partial void StartupBackupFailed(this ILogger logger);

    [LoggerMessage(EventId = 8010, Level = LogLevel.Information, Message = "Server running: world {WorldId}, state {State}")]
    public static partial void Running(this ILogger logger, Guid worldId, ServerRuntimeState state);

    [LoggerMessage(EventId = 8011, Level = LogLevel.Error, Message = "SAFE MODE: the journal cannot be written; players cannot change the world. Free the disk and type save.")]
    public static partial void WriterFaulted(this ILogger logger, Exception error);

    [LoggerMessage(EventId = 8012, Level = LogLevel.Information, Message = "The writer recovered; leaving safe mode")]
    public static partial void WriterRecovered(this ILogger logger);

    [LoggerMessage(EventId = 8013, Level = LogLevel.Information, Message = "checkpoint committed op={Operation}")]
    public static partial void CheckpointCommitted(this ILogger logger, ulong operation);

    [LoggerMessage(EventId = 8014, Level = LogLevel.Error, Message = "checkpoint failed op={Operation}; it is retried")]
    public static partial void CheckpointFailed(this ILogger logger, ulong operation);

    [LoggerMessage(EventId = 8015, Level = LogLevel.Critical, Message = "The server stops: {Reason}")]
    public static partial void Faulted(this ILogger logger, string reason, Exception? exception);

    [LoggerMessage(EventId = 8016, Level = LogLevel.Information, Message = "Shutdown step {Step}")]
    public static partial void ShutdownStep(this ILogger logger, string step);

    [LoggerMessage(EventId = 8017, Level = LogLevel.Warning, Message = "Shutdown step {Step} did not finish in {Timeout} s; going on")]
    public static partial void ShutdownStepTimedOut(this ILogger logger, string step, double timeout);

    [LoggerMessage(EventId = 8018, Level = LogLevel.Error, Message = "Shutdown step {Step} failed")]
    public static partial void ShutdownStepFailed(this ILogger logger, string step, Exception exception);

    [LoggerMessage(EventId = 8019, Level = LogLevel.Warning, Message = "A shutdown is already running; the request is ignored")]
    public static partial void ShutdownAlreadyRunning(this ILogger logger);

    [LoggerMessage(EventId = 8020, Level = LogLevel.Warning, Message = "The terrain store was left open: reads were still running; the end of the process closes it")]
    public static partial void TerrainStoreLeftOpen(this ILogger logger);

    [LoggerMessage(EventId = 8023, Level = LogLevel.Warning, Message = "The transport did not dispose in time and is left to the end of the process")]
    public static partial void TransportLeftOpen(this ILogger logger);

    [LoggerMessage(EventId = 8024, Level = LogLevel.Warning, Message = "A shutdown was requested while the server was starting; the start is given up")]
    public static partial void StartAborted(this ILogger logger);

    [LoggerMessage(EventId = 8025, Level = LogLevel.Error, Message = "A game system failed to shut down; the remaining systems are shut down regardless")]
    public static partial void AfterTerrainShutdownTaskFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8021, Level = LogLevel.Information, Message = "Stopped; state hash={Hash}")]
    public static partial void Stopped(this ILogger logger, string hash);

    [LoggerMessage(EventId = 8022, Level = LogLevel.Information, Message = "Shutdown requested ({Code})")]
    public static partial void ShutdownRequested(this ILogger logger, DisconnectCode code);

    [LoggerMessage(EventId = 8101, Level = LogLevel.Information, Message = "Player {Player} joined as {Name}")]
    public static partial void PlayerJoining(this ILogger logger, ulong player, string name);

    [LoggerMessage(EventId = 8102, Level = LogLevel.Information, Message = "Player {Player} is ready")]
    public static partial void PlayerReady(this ILogger logger, ulong player);

    [LoggerMessage(EventId = 8103, Level = LogLevel.Information, Message = "Player {Player} left: {Reason}")]
    public static partial void PlayerLeft(this ILogger logger, ulong player, DisconnectCode reason);

    [LoggerMessage(EventId = 8104, Level = LogLevel.Debug, Message = "Message {Type} from player {Player}")]
    public static partial void ClientMessage(this ILogger logger, ulong player, MessageType type);

    [LoggerMessage(EventId = 8105, Level = LogLevel.Warning, Message = "Player {Player} left but the simulation is faulted; the leave is not applied")]
    public static partial void LeaveSkipped(this ILogger logger, ulong player);

    [LoggerMessage(EventId = 8106, Level = LogLevel.Debug, Message = "Intent {ClientSeq} of player {Player} refused: the server is read-only")]
    public static partial void IntentRefusedReadOnly(this ILogger logger, ulong player, uint clientSeq);

    [LoggerMessage(EventId = 8107, Level = LogLevel.Error, Message = "A player observer failed while player {Player} was leaving; the leave goes on")]
    public static partial void ObserverFailed(this ILogger logger, ulong player, Exception exception);

    [LoggerMessage(EventId = 8108, Level = LogLevel.Error, Message = "A message handler failed on {Type} from player {Player}; the message is dropped")]
    public static partial void MessageHandlerFailed(this ILogger logger, ulong player, MessageType type, Exception exception);

    [LoggerMessage(EventId = 8201, Level = LogLevel.Information, Message = "Console command {Command}")]
    public static partial void ConsoleCommand(this ILogger logger, string command);

    [LoggerMessage(EventId = 8202, Level = LogLevel.Warning, Message = "Console input dropped: {Reason}")]
    public static partial void ConsoleInputDropped(this ILogger logger, string reason);

    [LoggerMessage(EventId = 8203, Level = LogLevel.Information, Message = "Console input ended; the server keeps running")]
    public static partial void ConsoleInputEnded(this ILogger logger);

    [LoggerMessage(EventId = 8204, Level = LogLevel.Warning, Message = "Reading the console failed")]
    public static partial void ConsoleReadFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8301, Level = LogLevel.Warning, Message = "Moved the damaged world to {Corrupt} and restored {Backup}")]
    public static partial void WorldRestored(this ILogger logger, string corrupt, string backup);
}
