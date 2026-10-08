using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence;

/// <summary>Column conventions and small helpers shared by the creator, the loader and the writer.</summary>
internal static class Sql
{
    internal const string MetaWorldId = "world_id";
    internal const string MetaGalaxySeed = "galaxy_seed";
    internal const string MetaNextEntityId = "next_entity_id";
    internal const string MetaNextPlayerId = "next_player_id";
    internal const string MetaLastCheckpointOpId = "last_checkpoint_op_id";
    internal const string MetaLastCheckpointTick = "last_checkpoint_tick";
    internal const string MetaGameVersion = "game_version";
    internal const string MetaCreatedUtc = "created_utc";

    /// <summary><see cref="ulong"/> stored in an INTEGER column.</summary>
    internal static long ToDb(ulong value) => unchecked((long)value);

    /// <summary>INTEGER column read back as <see cref="ulong"/>.</summary>
    internal static ulong FromDb(long value) => unchecked((ulong)value);

    internal static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string text)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = text;
        return command;
    }

    internal static void SetMeta(SqliteConnection connection, SqliteTransaction transaction, string key, object value)
    {
        using var command = Command(
            connection, transaction,
            "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;");
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    /// <summary>Deletes a file and its WAL side files; used after a failed creation.</summary>
    internal static void DeleteWithSideFiles(string path)
    {
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }
}
