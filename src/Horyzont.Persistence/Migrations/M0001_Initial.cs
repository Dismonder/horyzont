using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Migrations;

/// <summary>Initial schema of a world (K1-05).</summary>
internal sealed class M0001_Initial : IMigration
{
    private const string Sql = """
        CREATE TABLE meta (key TEXT PRIMARY KEY, value BLOB NOT NULL) WITHOUT ROWID;
        CREATE TABLE block_types (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE);
        CREATE TABLE materials (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE);
        CREATE TABLE planets (planet_id INTEGER PRIMARY KEY, params BLOB NOT NULL);
        CREATE TABLE terrain_blocks (
            planet_id INTEGER NOT NULL, lod INTEGER NOT NULL,
            block_x INTEGER NOT NULL, block_y INTEGER NOT NULL, block_z INTEGER NOT NULL,
            version INTEGER NOT NULL, data BLOB NOT NULL,
            PRIMARY KEY (planet_id, lod, block_x, block_y, block_z)) WITHOUT ROWID;
        CREATE TABLE entities (entity_id INTEGER PRIMARY KEY, kind INTEGER NOT NULL, data BLOB NOT NULL);
        CREATE TABLE construct_chunks (
            construct_id INTEGER NOT NULL, chunk_x INTEGER NOT NULL, chunk_y INTEGER NOT NULL, chunk_z INTEGER NOT NULL,
            data BLOB NOT NULL,
            PRIMARY KEY (construct_id, chunk_x, chunk_y, chunk_z)) WITHOUT ROWID;
        CREATE TABLE players (
            player_id INTEGER PRIMARY KEY, platform INTEGER NOT NULL, platform_id INTEGER NOT NULL,
            entity_id INTEGER NOT NULL, data BLOB NOT NULL,
            UNIQUE (platform, platform_id));
        CREATE TABLE journal (
            op_id INTEGER PRIMARY KEY, tick INTEGER NOT NULL, kind INTEGER NOT NULL,
            actor INTEGER, payload BLOB NOT NULL, crc INTEGER NOT NULL);
        CREATE TABLE checkpoints (
            checkpoint_id INTEGER PRIMARY KEY AUTOINCREMENT, barrier_op_id INTEGER NOT NULL,
            tick INTEGER NOT NULL, created_utc TEXT NOT NULL);
        """;

    /// <inheritdoc />
    public int Number => 1;

    /// <inheritdoc />
    public void Apply(SqliteConnection connection, SqliteTransaction transaction) =>
        WorldDatabase.Execute(connection, Sql, transaction);
}
