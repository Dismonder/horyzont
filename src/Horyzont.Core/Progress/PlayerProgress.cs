using Horyzont.Core.Ids;

namespace Horyzont.Core.Progress;

/// <summary>Progress of a player; in format 1 (K6-30) only the respawn point. A record exists only for a player with a respawn point.</summary>
/// <param name="Player">The player.</param>
/// <param name="RespawnBed">Bed that is the respawn point, null for none.</param>
/// <param name="Discovered">Item types the player discovered (K6-32), null for none.</param>
public sealed record PlayerProgress(PlayerId Player, BedRef? RespawnBed, ItemTypeBitSet? Discovered = null)
{
    /// <summary>Gets the discovered item types, never null.</summary>
    public ItemTypeBitSet DiscoveredItems => Discovered ?? ItemTypeBitSet.Empty;
}
