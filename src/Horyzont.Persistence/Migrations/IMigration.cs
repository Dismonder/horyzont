using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Migrations;

/// <summary>A numbered schema migration. Existing migrations are never edited; numbers are contiguous from 1.</summary>
public interface IMigration
{
    /// <summary>Number of the migration; after it runs <c>user_version</c> equals it.</summary>
    int Number { get; }

    /// <summary>Applies the migration inside <paramref name="transaction"/>; the runner sets <c>user_version</c> and commits.</summary>
    /// <param name="connection">Connection of the world.</param>
    /// <param name="transaction">The transaction of this migration.</param>
    void Apply(SqliteConnection connection, SqliteTransaction transaction);
}
