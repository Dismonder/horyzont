namespace Horyzont.Persistence.Terrain;

/// <summary>
/// Schema and limits of the remote client's terrain cache file (<c>cache/&lt;WorldId&gt;/terrain.db</c>). It is a database of its own,
/// never the world file: the rows are verified copies of server data that can always be dropped and fetched again.
/// </summary>
internal static class ClientTerrainCacheSchema
{
    /// <summary>Layout version stored in <see cref="MetaSchema"/>; a different value drops every cached block.</summary>
    internal const int SchemaVersion = 1;

    internal const string MetaSchema = "schema";

    /// <summary>World id, build hash, profile and planet parameters, semantics version: any change drops the cached blocks.</summary>
    internal const string MetaFingerprint = "fingerprint";

    internal const string Blocks = "client_terrain_blocks";

    /// <summary>Default limit of the blocks kept in the file.</summary>
    internal const long DefaultMaxDiskBytes = 512L * 1024 * 1024;

    /// <summary>Default limit of the blob bytes held in the memory overlay.</summary>
    internal const long DefaultMaxMemoryBytes = 64L * 1024 * 1024;

    /// <summary>Most block writes waiting for, or taken by, the writer at one time.</summary>
    internal const int MaxQueuedWrites = 256;

    internal static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(5);

    internal const string CreateSql = """
        CREATE TABLE IF NOT EXISTS client_meta (
            key TEXT NOT NULL PRIMARY KEY,
            value TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS client_terrain_blocks (
            planet INTEGER NOT NULL,
            lod INTEGER NOT NULL,
            x INTEGER NOT NULL,
            y INTEGER NOT NULL,
            z INTEGER NOT NULL,
            version INTEGER NOT NULL,
            hash INTEGER NOT NULL,
            used INTEGER NOT NULL,
            data BLOB NOT NULL,
            PRIMARY KEY (planet, lod, x, y, z)
        );
        """;

    internal const string SelectIndexSql =
        "SELECT planet, lod, x, y, z, version, hash, length(data) FROM client_terrain_blocks ORDER BY used, rowid;";

    internal const string SelectBlockSql =
        "SELECT version, hash, data FROM client_terrain_blocks WHERE planet = $p AND lod = $lod AND x = $x AND y = $y AND z = $z;";

    internal const string UpsertBlockSql =
        "INSERT OR REPLACE INTO client_terrain_blocks (planet, lod, x, y, z, version, hash, used, data) " +
        "VALUES ($p, $lod, $x, $y, $z, $version, $hash, $used, $data);";

    internal const string DeleteBlockSql =
        "DELETE FROM client_terrain_blocks WHERE planet = $p AND lod = $lod AND x = $x AND y = $y AND z = $z;";

    internal const string DeleteAllBlocksSql = "DELETE FROM client_terrain_blocks;";

    internal const string SelectMetaSql = "SELECT value FROM client_meta WHERE key = $key;";

    internal const string UpsertMetaSql =
        "INSERT INTO client_meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
}
