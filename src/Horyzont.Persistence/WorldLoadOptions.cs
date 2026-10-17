using Horyzont.Persistence.Migrations;

namespace Horyzont.Persistence;

/// <summary>Settings of <see cref="WorldLoader.Load"/>.</summary>
public sealed record WorldLoadOptions
{
    /// <summary>Schema migrations; tests substitute their own list.</summary>
    public IReadOnlyList<IMigration> Migrations { get; init; } = MigrationRunner.All;

    /// <summary>Directory of the pre-migration copy (<c>saves/&lt;WorldId&gt;/backups</c>).</summary>
    public required string BackupsDirectory { get; init; }

    /// <summary>Number of newest copies kept in <see cref="BackupsDirectory"/> after the pre-migration copy.</summary>
    public int BackupsToKeep { get; init; } = 10;

    /// <summary>Block types of the game content; missing keys get the next free numbers.</summary>
    public IReadOnlyList<string> BlockTypeKeys { get; init; } = [];

    /// <summary>Materials of the game content; missing keys get the next free numbers.</summary>
    public IReadOnlyList<string> MaterialKeys { get; init; } = [];

    /// <summary>Item types of the game content; missing keys get the next free numbers.</summary>
    public IReadOnlyList<string> ItemTypeKeys { get; init; } = [];
}
