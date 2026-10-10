namespace Horyzont.Persistence.Backup;

/// <summary>Result of <see cref="WorldBackups.RestoreLatest"/>.</summary>
/// <param name="CorruptPath">Where the damaged world file was moved.</param>
/// <param name="RestoredFrom">The backup that is the world file now.</param>
public sealed record WorldRestoreResult(string CorruptPath, string RestoredFrom);
