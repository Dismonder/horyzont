namespace Horyzont.Persistence;

/// <summary>Settings of the persistence writer.</summary>
public sealed record PersistenceOptions
{
    /// <summary>Longest time a journal batch waits after its first entry before it is committed.</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Number of journal entries that commits a batch immediately.</summary>
    public int MaxBatch { get; init; } = 256;

    /// <summary>Interval of the periodic backups made by the writer thread; <see cref="Timeout.InfiniteTimeSpan"/> disables them.</summary>
    public TimeSpan BackupInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Number of newest backups kept by the rotation.</summary>
    public int BackupsToKeep { get; init; } = 10;

    /// <summary>Version of the game written to the world by <see cref="WorldCreator"/> and by every checkpoint.</summary>
    public required string GameVersion { get; init; }

    /// <summary>Directory of the backups; null means <c>backups</c> next to the world file.</summary>
    public string? BackupsDirectory { get; init; }
}
