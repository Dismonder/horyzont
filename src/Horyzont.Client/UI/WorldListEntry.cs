namespace Horyzont.Client.UI;

/// <summary>File metadata only; presence never guarantees a healthy database.</summary>
/// <param name="WorldId">Directory world identifier.</param>
/// <param name="LastWriteUtc">Database write time or directory time when unavailable.</param>
/// <param name="DatabasePresent">Whether world.db exists and its metadata is accessible.</param>
/// <param name="Error">Entry access error, if any.</param>
public readonly record struct WorldListEntry(Guid WorldId, DateTimeOffset LastWriteUtc, bool DatabasePresent, string? Error);
