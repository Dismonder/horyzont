using System.Globalization;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;

using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// Version of the terrain edit semantics in the <c>meta</c> table (K2-09, gate decision D1): <c>"{SemanticsVersion}|voxel={voxel.ref}"</c>.
/// The server writes it after the startup checkpoint and before the first operation, so every journal entry after the checkpoint
/// was applied with the semantics saved here. A world without the key (created before K2-09) counts as version 1.
/// </summary>
public static class TerrainSemanticsMeta
{
    /// <summary>Key in the <c>meta</c> table.</summary>
    public const string MetaKey = "terrain_semantics";

    /// <summary>Semantics version of a world created before the key existed.</summary>
    public const ushort LegacyVersion = 1;

    /// <summary>Formats the semantics string.</summary>
    /// <param name="semanticsVersion">Terrain edit semantics version of the game.</param>
    /// <param name="voxelRef">Voxel Tools reference from <c>engine/engine.json</c>.</param>
    /// <returns>The string stored in <c>meta</c>.</returns>
    public static string Format(ushort semanticsVersion, string voxelRef)
    {
        ArgumentException.ThrowIfNullOrEmpty(voxelRef);
        ArgumentOutOfRangeException.ThrowIfZero(semanticsVersion);
        return string.Create(CultureInfo.InvariantCulture, $"{semanticsVersion}|voxel={voxelRef}");
    }

    /// <summary>Reads the saved semantics; a world without the key gets <see cref="LegacyVersion"/> with the current voxel reference.</summary>
    /// <param name="databasePath">World file.</param>
    /// <param name="currentVoxelRef">Voxel Tools reference of this build.</param>
    /// <returns>The saved semantics string.</returns>
    /// <exception cref="InvalidDataException">The value is not text.</exception>
    public static string Read(string databasePath, string currentVoxelRef)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentException.ThrowIfNullOrEmpty(currentVoxelRef);
        using var connection = WorldDatabase.OpenConnection(
            WorldDatabase.ConnectionString(Path.GetFullPath(databasePath), SqliteOpenMode.ReadOnly, pooling: false), readOnly: true);
        using var command = Sql.Command(connection, null, "SELECT value FROM meta WHERE key = $key;");
        command.Parameters.AddWithValue("$key", MetaKey);
        return command.ExecuteScalar() switch
        {
            null => Format(LegacyVersion, currentVoxelRef),
            string { Length: > 0 } text => text,
            _ => throw new InvalidDataException($"Meta {MetaKey} is not a non-empty text."),
        };
    }

    /// <summary>
    /// Writes the semantics in its own transaction (after the startup checkpoint, before the first operation). Uses its own
    /// connection; the persistence writer may be open, SQLite serializes the two writers.
    /// </summary>
    /// <param name="databasePath">World file.</param>
    /// <param name="semantics">The string from <see cref="Format"/>.</param>
    public static void Write(string databasePath, string semantics)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentException.ThrowIfNullOrEmpty(semantics);
        using var connection = WorldDatabase.OpenConnection(
            WorldDatabase.ConnectionString(Path.GetFullPath(databasePath), SqliteOpenMode.ReadWrite, pooling: false), readOnly: false);
        using var transaction = connection.BeginTransaction();
        Sql.SetMeta(connection, transaction, MetaKey, semantics);
        transaction.Commit();
    }

    /// <summary>Decides whether the journal tail may be replayed with the current semantics.</summary>
    /// <param name="savedSemantics">From <see cref="Read"/>.</param>
    /// <param name="currentSemantics">From <see cref="Format"/> with the values of this build.</param>
    /// <param name="tail">The journal tail of the loaded world.</param>
    /// <returns>Allowed when the semantics are equal or the tail has no terrain edit.</returns>
    public static TerrainTailCheck CheckTail(string savedSemantics, string currentSemantics, IReadOnlyList<JournalEntry> tail)
    {
        ArgumentNullException.ThrowIfNull(savedSemantics);
        ArgumentNullException.ThrowIfNull(currentSemantics);
        ArgumentNullException.ThrowIfNull(tail);
        var edits = 0;
        var firstEdit = OpId.None;
        foreach (var entry in tail)
        {
            if (OperationTerrain.TryGetTerrainEdit(entry.Operation, out _))
            {
                if (edits == 0)
                {
                    firstEdit = entry.Id;
                }

                edits++;
            }
        }

        var allowed = edits == 0 || string.Equals(savedSemantics, currentSemantics, StringComparison.Ordinal);
        return new TerrainTailCheck(
            allowed, savedSemantics, currentSemantics, tail.Count, tail.Count > 0 ? tail[0].Id : OpId.None, edits, firstEdit);
    }
}
