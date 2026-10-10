using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Migrations;

/// <summary>
/// Survival schema (K6-29W D1): item types, containers and the tables later survival packages fill (block entities, player progress,
/// harvested props). A world of this schema stores <c>next_container_id</c> in <c>meta</c>.
/// </summary>
internal sealed class M0003_Survival : IMigration
{
    private const string Sql = """
        CREATE TABLE item_types (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE);
        CREATE TABLE containers (container_id INTEGER PRIMARY KEY, data BLOB NOT NULL);
        CREATE TABLE block_entities (
            construct_id INTEGER NOT NULL, cell_x INTEGER NOT NULL, cell_y INTEGER NOT NULL, cell_z INTEGER NOT NULL,
            data BLOB NOT NULL,
            PRIMARY KEY (construct_id, cell_x, cell_y, cell_z)) WITHOUT ROWID;
        CREATE TABLE player_progress (player_id INTEGER PRIMARY KEY, data BLOB NOT NULL);
        CREATE TABLE harvested_props (
            planet_id INTEGER NOT NULL, prop_id INTEGER NOT NULL, tick INTEGER NOT NULL,
            PRIMARY KEY (planet_id, prop_id)) WITHOUT ROWID;
        INSERT INTO meta (key, value) VALUES ('next_container_id', 1);
        """;

    /// <inheritdoc />
    public int Number => 3;

    /// <inheritdoc />
    public void Apply(SqliteConnection connection, SqliteTransaction transaction) =>
        WorldDatabase.Execute(connection, Sql, transaction);
}
