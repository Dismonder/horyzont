using Horyzont.Core.Ids;

namespace Horyzont.Core.Progress;

/// <summary>Progress of a player; in format 1 (K6-30) only the respawn point. A record exists only for a player with a respawn point.</summary>
/// <param name="Player">The player.</param>
/// <param name="RespawnBed">Bed that is the respawn point, null for none.</param>
public sealed record PlayerProgress(PlayerId Player, BedRef? RespawnBed);
