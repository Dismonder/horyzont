using System.Globalization;

using Horyzont.Core.Ids;

namespace Horyzont.Persistence.Backup;

/// <summary>Owner-requested repairs of a world file that go beyond the automatic backups: restoring the newest copy and dropping the journal tail.</summary>
public static class WorldBackups
{
    /// <summary>Reason in the file name of the copy made before <see cref="DiscardJournalTail"/>.</summary>
    public const string PreRecoverReason = "pre-recover";

    /// <summary>
    /// Copies the world, then deletes every journal entry after the last checkpoint. The world is then the state of that checkpoint;
    /// the dropped operations are lost (they stay in the copy). Call it while no writer has the file open.
    /// </summary>
    /// <param name="worldPath">World file.</param>
    /// <param name="lastCheckpointOpId">Barrier of the last checkpoint (<see cref="LoadedWorld.LastCheckpointOpId"/>).</param>
    /// <param name="backupsDirectory">Directory of the copies.</param>
    /// <param name="backupsToKeep">Number of newest copies kept.</param>
    /// <param name="time">Clock of the copy name.</param>
    /// <returns>The path of the copy and the number of dropped entries.</returns>
    public static (string Backup, int Dropped) DiscardJournalTail(
        string worldPath, OpId lastCheckpointOpId, string backupsDirectory, int backupsToKeep, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(worldPath);
        ArgumentException.ThrowIfNullOrEmpty(backupsDirectory);
        ArgumentNullException.ThrowIfNull(time);
        using var database = WorldDatabase.Open(worldPath);
        var backup = new BackupService(backupsDirectory, backupsToKeep, time).CreateBackup(database.Connection, PreRecoverReason);
        using var command = Sql.Command(database.Connection, null, "DELETE FROM journal WHERE op_id > $barrier;");
        command.Parameters.AddWithValue("$barrier", Sql.ToDb(lastCheckpointOpId.Value));
        return (backup, command.ExecuteNonQuery());
    }

    /// <summary>
    /// Replaces a damaged world with its newest published backup: the damaged file (with its <c>-wal</c> and <c>-shm</c>) is moved to
    /// <c>world.db.corrupt-&lt;yyyyMMdd-HHmmss&gt;</c> next to it and the backup is copied to the world path.
    /// </summary>
    /// <param name="worldPath">World file.</param>
    /// <param name="backupsDirectory">Directory of the copies.</param>
    /// <param name="time">Clock of the name of the moved file.</param>
    /// <returns>The paths of the moved file and the restored backup; null when there is no published backup (nothing is touched).</returns>
    public static WorldRestoreResult? RestoreLatest(string worldPath, string backupsDirectory, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(worldPath);
        ArgumentException.ThrowIfNullOrEmpty(backupsDirectory);
        ArgumentNullException.ThrowIfNull(time);
        var copies = new BackupService(backupsDirectory, 1, time).List();
        if (copies.Count == 0)
        {
            return null;
        }

        var backup = copies[^1];
        var fullPath = Path.GetFullPath(worldPath);
        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var corrupt = $"{fullPath}.corrupt-{stamp}";
        var index = 1;
        while (File.Exists(corrupt))
        {
            corrupt = $"{fullPath}.corrupt-{stamp}-{index++.ToString(CultureInfo.InvariantCulture)}";
        }

        // Copy first under a temporary name: a failed copy leaves the damaged file where it was.
        var restoring = fullPath + ".restoring";
        File.Copy(backup, restoring, overwrite: true);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            if (File.Exists(fullPath + suffix))
            {
                File.Move(fullPath + suffix, corrupt + suffix);
            }
        }

        File.Move(restoring, fullPath);
        return new WorldRestoreResult(corrupt, backup);
    }
}
