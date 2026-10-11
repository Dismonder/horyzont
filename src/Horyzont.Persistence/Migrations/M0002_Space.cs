using Horyzont.Core.Serialization;
using Horyzont.Core.Space;
using Horyzont.Core.World;

using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Migrations;

/// <summary>
/// Space schema of K5 (K5-21 decision 12): tables <c>star_systems</c>, <c>markers</c> and <c>ship_state</c> (every field of a
/// marker and of a ship state lives in blobs 11 and 12, so K5-24 and K5-27 add no DDL), and in the same transaction the star
/// system of an existing world. A world with a galaxy seed and exactly one planet gets <see cref="StarSystemLayout.V1"/> around
/// that planet as its home (three <c>planets</c> rows, blob 4, and one <c>star_systems</c> row, blob 10); an empty file
/// (<see cref="WorldCreator"/> runs migrations before writing meta) and a world with another planet count stay without a star
/// system (the loader logs it). The explicit V1 freezes the behaviour of this migration.
/// </summary>
internal sealed class M0002_Space : IMigration
{
    private const string Ddl = """
        CREATE TABLE star_systems (system_id INTEGER PRIMARY KEY, params BLOB NOT NULL);
        CREATE TABLE markers (marker_id INTEGER PRIMARY KEY, data BLOB NOT NULL);
        CREATE TABLE ship_state (construct_id INTEGER PRIMARY KEY, data BLOB NOT NULL);
        """;

    /// <inheritdoc />
    public int Number => 2;

    /// <inheritdoc />
    public void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        WorldDatabase.Execute(connection, Ddl, transaction);
        if (!TryReadGalaxySeed(connection, transaction, out var seed) || !TryReadOnlyPlanet(connection, transaction, out var home))
        {
            return;
        }

        GeneratedStarSystem created;
        try
        {
            created = StarSystemGenesis.Create(seed, home, StarSystemLayout.V1);
        }
        catch (InvalidDataException)
        {
            // A body id collision (2^-64) or a home planet too large for the V1 zones: the world keeps running without a star
            // system, exactly as before K5-21; the loader logs that the world has none.
            return;
        }

        for (var k = 1; k < created.Bodies.Count; k++)
        {
            var planet = created.Bodies[k];
            using var insert = Sql.Command(connection, transaction, "INSERT INTO planets (planet_id, params) VALUES ($id, $params);");
            insert.Parameters.AddWithValue("$id", Sql.ToDb(planet.Id.Value));
            insert.Parameters.AddWithValue("$params", StateCodec.EncodePlanetBlob(planet));
            insert.ExecuteNonQuery();
        }

        WorldCreator.InsertStarSystem(connection, transaction, created.System);
    }

    private static bool TryReadGalaxySeed(SqliteConnection connection, SqliteTransaction transaction, out ulong seed)
    {
        using var command = Sql.Command(connection, transaction, "SELECT value FROM meta WHERE key = $key;");
        command.Parameters.AddWithValue("$key", Sql.MetaGalaxySeed);
        if (command.ExecuteScalar() is long value)
        {
            seed = Sql.FromDb(value);
            return true;
        }

        seed = 0;
        return false;
    }

    private static bool TryReadOnlyPlanet(SqliteConnection connection, SqliteTransaction transaction, out PlanetParams home)
    {
        using var command = Sql.Command(connection, transaction, "SELECT params FROM planets LIMIT 2;");
        using var reader = command.ExecuteReader();
        home = null!;
        if (!reader.Read())
        {
            return false;
        }

        var blob = (byte[])reader.GetValue(0);
        if (reader.Read())
        {
            return false;
        }

        // A damaged blob throws ProtocolViolationException and rolls the migration back; the loader reports the world as damaged.
        home = StateCodec.DecodePlanetBlob(blob);
        return true;
    }
}
