using System.Globalization;

using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Persistence.Migrations;

using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence;

/// <summary>Creates the file of a new world: schema, meta, registries and planets.</summary>
public static class WorldCreator
{
    /// <summary>Writes a world made by <c>WorldState.CreateNew</c> to a new file.</summary>
    /// <param name="path">World file; must not exist. The directory is created when missing.</param>
    /// <param name="world">The new world: no operations, constructs, players or terrain versions.</param>
    /// <param name="options">Persistence options (the game version is stored).</param>
    /// <param name="time">Clock for <c>created_utc</c>.</param>
    /// <exception cref="ArgumentException">The world is not new.</exception>
    /// <exception cref="IOException">The file already exists.</exception>
    public static void Create(string path, WorldState world, PersistenceOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        if (world.LastOpId != OpId.None || world.Constructs.Count != 0 || world.Players.Count != 0
            || world.TerrainVersions.Count != 0)
        {
            throw new ArgumentException("Only a world fresh from WorldState.CreateNew can be created.", nameof(world));
        }

        var database = WorldDatabase.CreateNew(path);
        var fullPath = database.Path;
        try
        {
            MigrationRunner.Run(database.Connection, MigrationRunner.All);
            Write(database.Connection, world, options.GameVersion, time);
        }
        catch
        {
            database.Dispose();
            Sql.DeleteWithSideFiles(fullPath);
            throw;
        }

        database.Dispose();
    }

    private static void Write(SqliteConnection connection, WorldState world, string gameVersion, TimeProvider time)
    {
        using var transaction = connection.BeginTransaction();
        Sql.SetMeta(connection, transaction, Sql.MetaWorldId, world.Id.Value.ToByteArray());
        Sql.SetMeta(connection, transaction, Sql.MetaGalaxySeed, Sql.ToDb(world.GalaxySeed));
        Sql.SetMeta(connection, transaction, Sql.MetaNextEntityId, Sql.ToDb(world.EntityIds.NextValue));
        Sql.SetMeta(connection, transaction, Sql.MetaNextPlayerId, Sql.ToDb(world.PlayerIds.NextValue));
        Sql.SetMeta(connection, transaction, Sql.MetaLastCheckpointOpId, 0L);
        Sql.SetMeta(connection, transaction, Sql.MetaLastCheckpointTick, 0L);
        Sql.SetMeta(connection, transaction, Sql.MetaGameVersion, gameVersion);
        Sql.SetMeta(connection, transaction, Sql.MetaCreatedUtc, time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));

        foreach (var entry in world.BlockTypes.Entries)
        {
            InsertRegistry(connection, transaction, "block_types", entry.Id, entry.Key);
        }

        foreach (var entry in world.Materials.Entries)
        {
            InsertRegistry(connection, transaction, "materials", entry.Id, entry.Key);
        }

        foreach (var planet in world.Planets.All)
        {
            using var command = Sql.Command(connection, transaction, "INSERT INTO planets (planet_id, params) VALUES ($id, $params);");
            command.Parameters.AddWithValue("$id", Sql.ToDb(planet.Id.Value));
            command.Parameters.AddWithValue("$params", StateCodec.EncodePlanetBlob(planet));
            command.ExecuteNonQuery();
        }

        if (world.StarSystem is { } system)
        {
            InsertStarSystem(connection, transaction, system);
        }

        transaction.Commit();
    }

    internal static void InsertStarSystem(SqliteConnection connection, SqliteTransaction transaction, StarSystemParams system)
    {
        using var command = Sql.Command(connection, transaction, "INSERT INTO star_systems (system_id, params) VALUES ($id, $params);");
        command.Parameters.AddWithValue("$id", Sql.ToDb(system.System.Value));
        command.Parameters.AddWithValue("$params", StateCodec.EncodeStarSystemBlob(system));
        command.ExecuteNonQuery();
    }

    internal static void InsertRegistry(SqliteConnection connection, SqliteTransaction transaction, string table, ushort id, string key)
    {
        using var command = Sql.Command(connection, transaction, $"INSERT INTO {table} (id, key) VALUES ($id, $key);");
        command.Parameters.AddWithValue("$id", (long)id);
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }
}
