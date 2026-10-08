using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Migrations;

/// <summary>Runs schema migrations, each in its own transaction that also sets <c>user_version</c>.</summary>
public static class MigrationRunner
{
    /// <summary>The production migrations in number order.</summary>
    public static IReadOnlyList<IMigration> All { get; } = [new M0001_Initial()];

    /// <summary>Checks that the numbers are exactly 1, 2, …, n in list order.</summary>
    /// <param name="migrations">The list.</param>
    /// <exception cref="InvalidOperationException">The list is empty or has a gap, a repeated number or a wrong order.</exception>
    public static void Validate(IReadOnlyList<IMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        if (migrations.Count == 0)
        {
            throw new InvalidOperationException("The migration list is empty.");
        }

        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i] is null || migrations[i].Number != i + 1)
            {
                throw new InvalidOperationException(
                    $"Migration at position {i} has number {migrations[i]?.Number}; numbers must be contiguous from 1 without repeats.");
            }
        }
    }

    /// <summary>Newest schema version of a list.</summary>
    /// <param name="migrations">The list.</param>
    /// <returns>The number of the last migration.</returns>
    /// <exception cref="InvalidOperationException">The list is not contiguous from 1.</exception>
    public static int LatestVersion(IReadOnlyList<IMigration> migrations)
    {
        Validate(migrations);
        return migrations[^1].Number;
    }

    /// <summary>Applies every migration with a number above the current <c>user_version</c>, each in its own transaction.</summary>
    /// <param name="connection">Open connection of the world.</param>
    /// <param name="migrations">The migration list.</param>
    /// <returns>The schema version after the run.</returns>
    /// <exception cref="InvalidOperationException">The list is not contiguous from 1.</exception>
    public static int Run(SqliteConnection connection, IReadOnlyList<IMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Validate(migrations);
        var version = WorldDatabase.ReadUserVersion(connection);
        foreach (var migration in migrations)
        {
            if (migration.Number <= version)
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            migration.Apply(connection, transaction);
            WorldDatabase.Execute(
                connection, $"PRAGMA user_version = {migration.Number.ToString(CultureInfo.InvariantCulture)};", transaction);
            transaction.Commit();
            version = migration.Number;
        }

        return version;
    }
}
