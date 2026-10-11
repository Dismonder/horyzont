namespace Horyzont.Client.UI;

/// <summary>Start result displayed without accessing a server runtime.</summary>
/// <param name="Kind">Result category.</param>
/// <param name="Message">Plain text diagnostic.</param>
/// <param name="BackupCount">Available backups.</param>
/// <param name="OpId">Failed operation.</param>
/// <param name="SchemaVersion">Database version.</param>
/// <param name="SupportedVersion">Supported database version.</param>
/// <param name="TerrainSemanticsMismatch">Whether checkpoint recovery can be offered.</param>
public sealed record UiStartOutcome(UiStartKind Kind, string Message, int BackupCount, ulong? OpId,
    int? SchemaVersion, int? SupportedVersion, bool TerrainSemanticsMismatch);
