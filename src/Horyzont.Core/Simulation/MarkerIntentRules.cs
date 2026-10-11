using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Validation of marker intents (K5-27 decision 5). The applier knows no actor, so ownership is decided here. A foreign private
/// marker behaves like a missing one (the same <see cref="RejectReason.InvalidParameters"/>), so its existence never leaks.
/// </summary>
internal static class MarkerIntentRules
{
    /// <summary>True when the parameters are well formed on their own (no state needed).</summary>
    internal static bool HasValidParameters(Intent intent) => intent switch
    {
        MarkerSetIntent set => !set.Planet.Equals(default(PlanetId)) && MarkerRules.IsValidPosition(set.Position) && MarkerRules.IsValidName(set.Name)
            && MarkerRules.IsValidStyle(set.Icon, set.Color) && set.Marker.Value <= MarkerIdAllocator.MaxValue,
        MarkerRemoveIntent remove => !remove.Marker.IsNone,
        _ => false,
    };

    /// <summary>Checks the state-dependent rules of a marker intent.</summary>
    internal static RejectReason Validate(WorldState state, PlayerId player, Intent intent)
    {
        switch (intent)
        {
            case MarkerSetIntent set:
                if (!state.Planets.TryGet(set.Planet, out _))
                {
                    return RejectReason.InvalidParameters;
                }

                if (set.Marker.IsNone)
                {
                    var count = set.Shared ? state.Markers.CountShared : state.Markers.CountPrivate(player);
                    var limit = set.Shared ? MarkerRules.MaxShared : MarkerRules.MaxPrivatePerPlayer;
                    if (state.MarkerIds.NextValue > MarkerIdAllocator.MaxValue)
                    {
                        return RejectReason.MarkerLimit;
                    }

                    return count >= limit ? RejectReason.MarkerLimit : RejectReason.None;
                }

                return state.Markers.TryGet(set.Marker, out var existing) && CanChange(existing.Owner, player) && existing.IsShared == set.Shared
                    ? RejectReason.None
                    : RejectReason.InvalidParameters;
            case MarkerRemoveIntent remove:
                return state.Markers.TryGet(remove.Marker, out var target) && CanChange(target.Owner, player)
                    ? RejectReason.None
                    : RejectReason.InvalidParameters;
            default:
                return RejectReason.InvalidParameters;
        }
    }

    private static bool CanChange(PlayerId? owner, PlayerId player) => owner is null || owner == player;
}
