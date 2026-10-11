using Horyzont.Persistence.Faults;

using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence;

/// <summary>Log messages of the persistence layer.</summary>
internal static partial class PersistenceLog
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "World {Path} loaded: checkpoint barrier {OpId}, {TailCount} journal entries to replay")]
    internal static partial void WorldLoaded(ILogger logger, string path, ulong opId, int tailCount);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Migrating world schema {From} -> {To}; copy saved to {Backup}")]
    internal static partial void PreMigrationBackup(ILogger logger, int from, int to, string backup);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "Registry {Table}: new key {Key} got id {Id}")]
    internal static partial void RegistryKeyAdded(ILogger logger, string table, string key, int id);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning, Message = "World {Path} has no star system ({PlanetCount} planets); only the home planet is placed")]
    internal static partial void NoStarSystem(ILogger logger, string path, int planetCount);

    [LoggerMessage(EventId = 110, Level = LogLevel.Critical, Message = "Persistence writer faulted; journal up to operation {DurableOpId} is durable, {Pending} entries wait in memory")]
    internal static partial void WriterFaulted(ILogger logger, Exception exception, ulong durableOpId, int pending);

    [LoggerMessage(EventId = 111, Level = LogLevel.Error, Message = "Checkpoint at barrier {OpId} failed")]
    internal static partial void CheckpointFailed(ILogger logger, ulong opId);

    [LoggerMessage(EventId = 112, Level = LogLevel.Information, Message = "Checkpoint at barrier {OpId} committed")]
    internal static partial void CheckpointCommitted(ILogger logger, ulong opId);

    [LoggerMessage(EventId = 113, Level = LogLevel.Warning, Message = "Persistence fault cleared; retrying {Pending} journal entries from operation {FirstOpId}")]
    internal static partial void FaultCleared(ILogger logger, int pending, ulong firstOpId);

    [LoggerMessage(EventId = 114, Level = LogLevel.Critical, Message = "Persistence writer stopped by a simulated crash at {Point}")]
    internal static partial void SimulatedCrash(ILogger logger, FaultPoint point);

    [LoggerMessage(EventId = 115, Level = LogLevel.Information, Message = "Backup {Path} created ({Reason})")]
    internal static partial void BackupCreated(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 116, Level = LogLevel.Error, Message = "Backup ({Reason}) failed; the world itself is not affected")]
    internal static partial void BackupFailed(ILogger logger, Exception exception, string reason);

    [LoggerMessage(EventId = 117, Level = LogLevel.Information, Message = "Skipping journal entries up to operation {OpId}: already durable before the fault")]
    internal static partial void SkippedDurable(ILogger logger, ulong opId);

    [LoggerMessage(EventId = 118, Level = LogLevel.Warning, Message = "Opening {Path} hit a transient I/O error (SQLite {ExtendedCode}); retry {Attempt} in {DelayMs} ms")]
    internal static partial void OpenRetried(ILogger logger, string path, int attempt, int extendedCode, int delayMs);
}
