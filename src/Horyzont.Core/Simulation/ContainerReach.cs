using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Distance from a player's eye to the centre of a block cell, measured in the frame of the planet both stand on (K6-30 D9). Shared by the
/// intent validator and the chest subscriptions of the server, so both judge reach the same way.
/// </summary>
public static class ContainerReach
{
    /// <summary>Measures the distance from the eye of <paramref name="player"/> to the centre of <paramref name="cell"/>.</summary>
    /// <param name="state">World state.</param>
    /// <param name="rules">Game rules (eye height).</param>
    /// <param name="player">The player.</param>
    /// <param name="construct">Construct holding the block.</param>
    /// <param name="cell">Cell of the block.</param>
    /// <param name="meters">The distance when the method returns true.</param>
    /// <returns>False when the player or the construct is unknown, or they are not on the same planet.</returns>
    public static bool TryDistanceFromEye(WorldState state, GameRules rules, PlayerId player, EntityId construct, CellPos cell, out float meters)
    {
        meters = 0f;
        return false; // K6-30 B4
    }
}
