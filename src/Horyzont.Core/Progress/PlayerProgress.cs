using Horyzont.Core.Ids;

namespace Horyzont.Core.Progress;

/// <summary>
/// Progress of a player: the respawn point (K6-30), the discovered item types (K6-32) and the food slots (K6-33). A record exists if and only if at
/// least one field differs from its default (a respawn point, a discovery or food, see <see cref="IsEmpty"/>); an empty set of discoveries is the same
/// as none and is stored as null.
/// </summary>
/// <param name="Player">The player.</param>
/// <param name="RespawnBed">Bed that is the respawn point, null for none.</param>
/// <param name="Discovered">Item types the player discovered (K6-32), null for none; an empty set is normalized to null.</param>
/// <param name="Food">The three food slots (K6-33), all empty by default.</param>
public sealed record PlayerProgress(PlayerId Player, BedRef? RespawnBed, ItemTypeBitSet? Discovered = null, FoodSlots Food = default)
{
    private readonly ItemTypeBitSet? _discovered = Normalize(Discovered);

    /// <summary>Gets the discovered item types; null for none (an empty set is normalized to null).</summary>
    public ItemTypeBitSet? Discovered
    {
        get => _discovered;
        init => _discovered = Normalize(value);
    }

    /// <summary>Gets the discovered item types, never null.</summary>
    public ItemTypeBitSet DiscoveredItems => _discovered ?? ItemTypeBitSet.Empty;

    /// <summary>Gets a value indicating whether every field has its default value: no respawn point, no discoveries and no food. Such a record must not exist (D4).</summary>
    public bool IsEmpty => RespawnBed is null && _discovered is null && Food.IsEmpty;

    private static ItemTypeBitSet? Normalize(ItemTypeBitSet? set) => set is { IsEmpty: true } ? null : set;
}
