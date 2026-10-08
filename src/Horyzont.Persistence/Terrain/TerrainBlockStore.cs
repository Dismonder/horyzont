using Horyzont.Core.World.Terrain;

using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// Reads saved terrain blocks for the game's voxel stream. Thread-safe: every call takes its own read-only connection from the
/// <c>Microsoft.Data.Sqlite</c> pool, and WAL readers do not block the writer. Blocks changed but not yet saved are held by the
/// game adapter, which checks them before calling <see cref="TryLoad"/>.
/// </summary>
public sealed class TerrainBlockStore
{
    private readonly string _connectionString;

    /// <summary>Creates the store.</summary>
    /// <param name="databasePath">World file.</param>
    public TerrainBlockStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        _connectionString = WorldDatabase.ConnectionString(Path.GetFullPath(databasePath), SqliteOpenMode.ReadOnly, pooling: true);
    }

    /// <summary>Reads the version and data of a saved block in one statement, so the pair is always consistent.</summary>
    /// <param name="key">Block key.</param>
    /// <param name="version">Saved version.</param>
    /// <param name="data">Saved data.</param>
    /// <returns>False when the block was never saved (the generator produces it).</returns>
    public bool TryLoad(TerrainBlockKey key, out uint version, out byte[] data)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        WorldDatabase.ApplyPragmas(connection, readOnly: true);
        using var command = Sql.Command(
            connection, null,
            "SELECT version, data FROM terrain_blocks WHERE planet_id = $p AND lod = $lod AND block_x = $x AND block_y = $y AND block_z = $z;");
        command.Parameters.AddWithValue("$p", Sql.ToDb(key.Planet.Value));
        command.Parameters.AddWithValue("$lod", (long)key.Lod);
        command.Parameters.AddWithValue("$x", (long)key.X);
        command.Parameters.AddWithValue("$y", (long)key.Y);
        command.Parameters.AddWithValue("$z", (long)key.Z);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            version = 0;
            data = [];
            return false;
        }

        version = checked((uint)reader.GetInt64(0));
        data = (byte[])reader.GetValue(1);
        return true;
    }
}
