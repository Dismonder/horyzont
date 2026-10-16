using Horyzont.Core.Ids;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Checkpoints;

/// <summary>The progress of a player in a checkpoint (blob <c>PlayerProgress</c>, 22); in version 1 only the respawn point.</summary>
/// <param name="Player">The player.</param>
/// <param name="RespawnBed">Bed that is the respawn point, null for none.</param>
/// <param name="Discovered">Item types the player discovered (K6-32, blob 22 version 2), null for none.</param>
public sealed record PlayerProgressRecord(PlayerId Player, BedRef? RespawnBed, ItemTypeBitSet? Discovered = null);
