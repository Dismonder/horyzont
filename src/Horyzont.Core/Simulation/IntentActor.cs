using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// The eye of an acting player in the frame of its planet (README Y9, K5-25 decision 3, R6): standing — feet plus the eye height along
/// the local up; seated — the seated eye of <see cref="SeatPose"/> in the construct, expressed in the planet frame. The validator and
/// the dev console (<c>seat</c>) use the same rule.
/// </summary>
public static class IntentActor
{
    /// <summary>The eye of <paramref name="player"/> in the frame of its planet.</summary>
    /// <param name="state">The world.</param>
    /// <param name="rules">Game rules (eye height, seat rules).</param>
    /// <param name="player">The player.</param>
    /// <param name="planet">Receives the planet.</param>
    /// <param name="eye">Receives the eye in the planet frame.</param>
    /// <returns>False when the frame of the player has no planet (the system frame, P6).</returns>
    public static bool TryGetEye(WorldState state, GameRules rules, PlayerState player, out PlanetId planet, out Vector3 eye)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(player);
        if (player.Seat is { } seat && player.Frame.Kind == FrameKind.Construct && state.Constructs.TryGet(new EntityId(player.Frame.Id), out var seatConstruct))
        {
            var local = SeatPose.Eye(seat, seatConstruct.GetBlock(seat).Orientation, rules.Seats);
            return state.TryToPlanetFrame(player.Frame, local, out planet, out eye);
        }

        if (!state.TryToPlanetFrame(player.Frame, player.Position, out planet, out var feet))
        {
            eye = default;
            return false;
        }

        eye = feet + (TerrainMath.UpAt(feet) * rules.EyeHeight);
        return true;
    }
}
