using System.IO.Hashing;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;
using Horyzont.Persistence.Backup;
using Horyzont.Persistence.Migrations;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Horyzont.Persistence;

/// <summary>
/// Reads a world: integrity check, migrations after a copy, registry growth, state of the last checkpoint and the checked
/// journal tail. Runs before the writer thread exists.
/// </summary>
public static class WorldLoader
{
    /// <summary>Loads the world file.</summary>
    /// <param name="path">World file.</param>
    /// <param name="options">Migrations, backups directory and registry keys of the game content.</param>
    /// <param name="time">Clock of the pre-migration copy name.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The world and its journal tail.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="WorldCorruptedException">The file fails the integrity check or holds damaged or inconsistent data.</exception>
    /// <exception cref="WorldFromNewerVersionException">The schema is newer than the newest migration.</exception>
    /// <exception cref="InvalidOperationException">The migration list is not contiguous from 1, or the content has too many registry keys.</exception>
    public static LoadedWorld Load(string path, WorldLoadOptions options, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        var latest = MigrationRunner.LatestVersion(options.Migrations);
        using var database = WorldDatabase.Open(path, logger);
        var connection = database.Connection;
        try
        {
            var check = database.QuickCheck();
            if (check != "ok")
            {
                throw new WorldCorruptedException($"Integrity check failed: {check}");
            }

            var version = database.UserVersion;
            if (version == 0)
            {
                throw new WorldCorruptedException("The file has no world schema (user_version 0).");
            }

            if (version > latest)
            {
                throw new WorldFromNewerVersionException(version, latest);
            }

            if (version == 2)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'item_types';";
                if ((long)command.ExecuteScalar()! != 0)
                {
                    throw new WorldCorruptedException(
                        "Unsupported pre-merge Survival world (schema 2). This version expects Space at schema 2 and Survival at schema 3; explicit conversion is required.");
                }
            }

            if (version < latest)
            {
                var backup = new BackupService(options.BackupsDirectory, options.BackupsToKeep, time).CreateBackup(connection, "pre-migration");
                PersistenceLog.PreMigrationBackup(logger, version, latest, backup);
                MigrationRunner.Run(connection, options.Migrations);
            }

            GrowRegistries(connection, "block_types", options.BlockTypeKeys, ProtocolConstants.MaxBlockTypes, logger);
            GrowRegistries(connection, "materials", options.MaterialKeys, ProtocolConstants.MaxMaterials, logger);
            GrowRegistries(connection, "item_types", options.ItemTypeKeys, ProtocolConstants.MaxItemTypes, logger);
            var orphans = new List<ulong>();
            var loaded = ReadWorld(connection, orphans);
            foreach (var orphan in orphans)
            {
                PersistenceLog.ShipStateWithoutConstruct(logger, orphan);
            }
            if (loaded.State.StarSystem is null)
            {
                PersistenceLog.NoStarSystem(logger, database.Path, loaded.State.Planets.All.Count);
            }

            PersistenceLog.WorldLoaded(logger, database.Path, loaded.LastCheckpointOpId.Value, loaded.JournalTail.Count);
            return loaded;
        }
        catch (SqliteException e) when (WorldDatabase.IsCorruption(e))
        {
            throw new WorldCorruptedException($"'{database.Path}' is damaged.", e);
        }
    }

    private static void GrowRegistries(SqliteConnection connection, string table, IReadOnlyList<string> keys, int maxId, ILogger logger)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(StringComparer.Ordinal);
        long maxExisting = 0;
        using (var select = Sql.Command(connection, null, $"SELECT id, key FROM {table};"))
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                maxExisting = Math.Max(maxExisting, reader.GetInt64(0));
                known.Add(reader.GetString(1));
            }
        }

        using var transaction = connection.BeginTransaction();
        var next = maxExisting + 1;
        foreach (var key in keys)
        {
            if (!known.Add(key))
            {
                continue;
            }

            if (next > maxId)
            {
                throw new InvalidOperationException($"The game content has more than {maxId} entries for {table}.");
            }

            WorldCreator.InsertRegistry(connection, transaction, table, (ushort)next, key);
            PersistenceLog.RegistryKeyAdded(logger, table, key, (int)next);
            next++;
        }

        transaction.Commit();
    }

    private static LoadedWorld ReadWorld(SqliteConnection connection, List<ulong> orphanShipStates)
    {
        var meta = ReadMeta(connection);
        try
        {
            var worldId = new WorldId(new Guid(MetaBytes(meta, Sql.MetaWorldId)));
            var lastCheckpoint = new OpId(Sql.OpIdFromDb(MetaInteger(meta, Sql.MetaLastCheckpointOpId)));
            var builder = new WorldStateBuilder(worldId, Sql.FromDb(MetaInteger(meta, Sql.MetaGalaxySeed)));
            builder.SetProgress(
                lastCheckpoint,
                new Tick(Sql.FromDb(MetaInteger(meta, Sql.MetaLastCheckpointTick))),
                Sql.FromDb(MetaInteger(meta, Sql.MetaNextEntityId)),
                Sql.FromDb(MetaInteger(meta, Sql.MetaNextPlayerId)));
            ReadRegistry(connection, "block_types", entry => builder.AddBlockType(entry));
            ReadRegistry(connection, "materials", entry => builder.AddMaterial(entry));
            ReadRegistry(connection, "item_types", entry => builder.AddItemType(entry));
            builder.SetNextContainerId(meta.ContainsKey(Sql.MetaNextContainerId) ? Sql.FromDb(MetaInteger(meta, Sql.MetaNextContainerId)) : 1UL);
            ReadPlanets(connection, builder);
            ReadStarSystem(connection, builder);
            ReadConstructs(connection, builder);
            ReadPlayers(connection, builder);
            // K5-27: worlds created before markers have no key; the counter starts at 1.
            builder.SetNextMarkerId(meta.TryGetValue(Sql.MetaNextMarkerId, out var nextMarker) && nextMarker is long markerNumber
                ? Sql.FromDb(markerNumber)
                : 1);
            ReadMarkers(connection, builder);
            ReadContainers(connection, builder);
            ReadBlockEntities(connection, builder);
            ReadPlayerProgress(connection, builder);
            ReadHarvestedProps(connection, builder);
            ReadTerrainVersions(connection, builder);
            ReadShipStates(connection, builder, orphanShipStates);
            var state = builder.Build();
            var tail = ReadJournal(connection, lastCheckpoint);
            return new LoadedWorld(state, tail, lastCheckpoint, MetaText(meta, Sql.MetaGameVersion));
        }
        catch (Exception e) when (e is ProtocolViolationException or InvalidDataException or InvalidCastException or OverflowException
                                      or ArgumentException or FormatException)
        {
            throw new WorldCorruptedException($"The world data is damaged: {e.Message}", e);
        }
    }

    private static Dictionary<string, object> ReadMeta(SqliteConnection connection)
    {
        var meta = new Dictionary<string, object>(StringComparer.Ordinal);
        using var command = Sql.Command(connection, null, "SELECT key, value FROM meta;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            meta[reader.GetString(0)] = reader.GetValue(1);
        }

        return meta;
    }

    private static long MetaInteger(Dictionary<string, object> meta, string key) =>
        meta.TryGetValue(key, out var value) && value is long number
            ? number
            : throw new InvalidDataException($"Meta key '{key}' is missing or not an integer.");

    private static string MetaText(Dictionary<string, object> meta, string key) =>
        meta.TryGetValue(key, out var value) && value is string text
            ? text
            : throw new InvalidDataException($"Meta key '{key}' is missing or not text.");

    private static byte[] MetaBytes(Dictionary<string, object> meta, string key) =>
        meta.TryGetValue(key, out var value) && value is byte[] { Length: 16 } bytes
            ? bytes
            : throw new InvalidDataException($"Meta key '{key}' is missing or not 16 bytes.");

    private static void ReadRegistry(SqliteConnection connection, string table, Action<RegistryEntry> add)
    {
        using var command = Sql.Command(connection, null, $"SELECT id, key FROM {table} ORDER BY id;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            if (id is < 1 or > ushort.MaxValue)
            {
                throw new InvalidDataException($"Registry {table} has id {id}.");
            }

            add(new RegistryEntry((ushort)id, reader.GetString(1)));
        }
    }

    private static void ReadPlanets(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(connection, null, "SELECT planet_id, params FROM planets ORDER BY planet_id;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = Sql.FromDb(reader.GetInt64(0));
            var planet = StateCodec.DecodePlanetBlob((byte[])reader.GetValue(1));
            if (planet.Id.Value != id)
            {
                throw new InvalidDataException($"Planet row {id} holds planet {planet.Id.Value}.");
            }

            builder.AddPlanet(planet);
        }
    }

    private static void ReadStarSystem(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(connection, null, "SELECT system_id, params FROM star_systems ORDER BY system_id;");
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return;
        }

        var id = Sql.FromDb(reader.GetInt64(0));
        var system = StateCodec.DecodeStarSystemBlob((byte[])reader.GetValue(1));
        if (system.System.Value != id)
        {
            throw new InvalidDataException($"Star system row {id} holds system {system.System.Value}.");
        }

        if (reader.Read())
        {
            throw new InvalidDataException("A world has at most one star system.");
        }

        builder.SetStarSystem(system);
    }

    private static void ReadConstructs(SqliteConnection connection, WorldStateBuilder builder)
    {
        using (var command = Sql.Command(connection, null, "SELECT entity_id, kind, data FROM entities ORDER BY entity_id;"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = Sql.FromDb(reader.GetInt64(0));
                var kind = reader.GetInt64(1);
                if (kind != (long)EntityKind.Construct)
                {
                    throw new InvalidDataException($"Entity {id} has unknown kind {kind}.");
                }

                var header = StateCodec.DecodeConstructHeaderBlob((byte[])reader.GetValue(2));
                if (header.Id.Value != id)
                {
                    throw new InvalidDataException($"Entity row {id} holds construct {header.Id.Value}.");
                }

                builder.AddConstruct(header);
            }
        }

        using (var command = Sql.Command(
            connection, null,
            "SELECT construct_id, chunk_x, chunk_y, chunk_z, data FROM construct_chunks ORDER BY construct_id, chunk_x, chunk_y, chunk_z;"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = new ConstructChunkKey(
                    new EntityId(Sql.FromDb(reader.GetInt64(0))),
                    new ChunkPos(checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(3))));
                builder.AddChunk(new ConstructChunkRecord(key, StateCodec.DecodeChunkBlob((byte[])reader.GetValue(4))));
            }
        }
    }

    private static void ReadPlayers(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(
            connection, null, "SELECT player_id, platform, platform_id, entity_id, data FROM players ORDER BY player_id;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = Sql.FromDb(reader.GetInt64(0));
            var record = StateCodec.DecodePlayerBlob((byte[])reader.GetValue(4));
            if (record.Player.Value != id
                || (long)record.Identity.Platform != reader.GetInt64(1)
                || Sql.ToDb(record.Identity.Value) != reader.GetInt64(2)
                || Sql.ToDb(record.Entity.Value) != reader.GetInt64(3))
            {
                throw new InvalidDataException($"Player row {id} does not match its blob.");
            }

            builder.AddPlayer(record);
        }
    }

    private static void ReadMarkers(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(connection, null, "SELECT marker_id, data FROM markers ORDER BY marker_id;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = Sql.FromDb(reader.GetInt64(0));
            var record = StateCodec.DecodeMarkerBlob((byte[])reader.GetValue(1));
            if (record.Id.Value != id)
            {
                throw new InvalidDataException($"Marker row {id} does not match its blob.");
            }

            builder.AddMarker(record);
        }
    }

    /// <summary>K5-24 decision 16: flight states by construct id; a row of a construct that does not exist is skipped (Warning).</summary>
    private static void ReadShipStates(SqliteConnection connection, WorldStateBuilder builder, List<ulong> orphans)
    {
        using var command = Sql.Command(
            connection, null,
            "SELECT s.construct_id, s.data, e.entity_id FROM ship_state s "
            + "LEFT JOIN entities e ON e.entity_id = s.construct_id AND e.kind = $kind ORDER BY s.construct_id;");
        command.Parameters.AddWithValue("$kind", (long)EntityKind.Construct);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = Sql.FromDb(reader.GetInt64(0));
            var record = StateCodec.DecodeShipStateBlob((byte[])reader.GetValue(1));
            if (record.Construct.Value != id)
            {
                throw new InvalidDataException($"Ship state row {id} does not match its blob.");
            }

            if (reader.IsDBNull(2))
            {
                orphans.Add(id);
                continue;
            }

            builder.AddShipState(record);
        }
    }

    private static void ReadContainers(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(connection, null, "SELECT container_id, data FROM containers ORDER BY container_id;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = Sql.FromDb(reader.GetInt64(0));
            var record = StateCodec.DecodeContainerBlob((byte[])reader.GetValue(1));
            if (record.Id.Value != id)
            {
                throw new InvalidDataException($"Container row {id} holds container {record.Id.Value}.");
            }

            builder.AddContainer(record);
        }
    }

    private static void ReadBlockEntities(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(
            connection, null,
            "SELECT construct_id, cell_x, cell_y, cell_z, data FROM block_entities ORDER BY construct_id, cell_x, cell_y, cell_z;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var construct = Sql.FromDb(reader.GetInt64(0));
            var cell = new CellPos(checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(3)));
            var record = StateCodec.DecodeBlockEntityBlob((byte[])reader.GetValue(4));
            if (record.Construct.Value != construct || record.Cell != cell)
            {
                throw new InvalidDataException($"Block state row {construct}/{cell} holds the state of {record.Construct.Value}/{record.Cell}.");
            }

            builder.AddBlockEntity(record);
        }
    }

    private static void ReadPlayerProgress(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(connection, null, "SELECT player_id, data FROM player_progress ORDER BY player_id;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var player = Sql.FromDb(reader.GetInt64(0));
            var record = StateCodec.DecodePlayerProgressBlob((byte[])reader.GetValue(1));
            if (record.Player.Value != player)
            {
                throw new InvalidDataException($"Progress row {player} holds the progress of player {record.Player.Value}.");
            }

            builder.AddPlayerProgress(record);
        }
    }

    private static void ReadHarvestedProps(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(connection, null, "SELECT planet_id, prop_id, tick FROM harvested_props ORDER BY planet_id, prop_id;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // The tick is a ulong stored bit for bit in the signed INTEGER; the planet, the reserved bits and the grid are checked by the builder.
            builder.AddHarvestedProp(new HarvestedPropRecord(
                new PlanetId(Sql.FromDb(reader.GetInt64(0))), new PropId(Sql.FromDb(reader.GetInt64(1))), new Tick(Sql.FromDb(reader.GetInt64(2)))));
        }
    }

    private static void ReadTerrainVersions(SqliteConnection connection, WorldStateBuilder builder)
    {
        using var command = Sql.Command(
            connection, null,
            "SELECT planet_id, lod, block_x, block_y, block_z, version FROM terrain_blocks ORDER BY planet_id, lod, block_x, block_y, block_z;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = new TerrainBlockKey(
                new PlanetId(Sql.FromDb(reader.GetInt64(0))),
                checked((byte)reader.GetInt64(1)),
                checked((int)reader.GetInt64(2)),
                checked((int)reader.GetInt64(3)),
                checked((int)reader.GetInt64(4)));
            builder.AddTerrainVersion(new TerrainBlockVersion(key, checked((uint)reader.GetInt64(5))));
        }
    }

    private static List<JournalEntry> ReadJournal(SqliteConnection connection, OpId lastCheckpoint)
    {
        var tail = new List<JournalEntry>();
        var expected = lastCheckpoint.Next;
        using var command = Sql.Command(connection, null, "SELECT op_id, tick, kind, actor, payload, crc FROM journal ORDER BY op_id;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var stored = reader.GetInt64(0);
            if (stored < 0)
            {
                throw new WorldCorruptedException(expected, $"the journal has operation id {stored}, above the supported ceiling.");
            }

            var id = new OpId((ulong)stored);
            if (id != expected)
            {
                throw new WorldCorruptedException(expected, $"the journal continues with operation {id.Value}.");
            }

            var payload = (byte[])reader.GetValue(4);
            if (Crc32.HashToUInt32(payload) != Sql.FromDb(reader.GetInt64(5)))
            {
                throw new WorldCorruptedException(id, "CRC mismatch.");
            }

            Operation operation;
            try
            {
                var frame = new ByteReader(payload);
                operation = OperationCodec.Read(ref frame);
                frame.EnsureEnd();
            }
            catch (ProtocolViolationException e)
            {
                throw new WorldCorruptedException(id, e.Message, e);
            }

            if ((long)operation.Kind != reader.GetInt64(2))
            {
                throw new WorldCorruptedException(id, "the kind column does not match the payload.");
            }

            PlayerId? actor = reader.IsDBNull(3) ? null : new PlayerId(Sql.FromDb(reader.GetInt64(3)));
            tail.Add(new JournalEntry(id, new Tick(Sql.FromDb(reader.GetInt64(1))), actor, operation));
            expected = expected.Next;
        }

        return tail;
    }
}
