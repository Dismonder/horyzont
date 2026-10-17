using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

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
        return state.Players.TryGet(player, out var playerState) && TryGetEye(state, rules, playerState, out var playerPlanet, out var eye)
            && TryDistanceFrom(state, playerPlanet, eye, construct, cell, out meters);
    }

    /// <summary>Distance from an eye already measured in the frame of <paramref name="playerPlanet"/> to the centre of <paramref name="cell"/>.</summary>
    internal static bool TryDistanceFrom(WorldState state, PlanetId playerPlanet, Vector3 eye, EntityId construct, CellPos cell, out float meters)
    {
        meters = 0f;
        if (!TryCellCenter(state, construct, cell, out var planet, out var centre) || planet != playerPlanet)
        {
            return false;
        }

        meters = Vector3.Distance(eye, centre);
        return true;
    }

    /// <summary>Measures the distance from the eye of <paramref name="player"/> to a persistent entity, such as a tombstone (K6-35 D13), on the planet both stand on.</summary>
    /// <param name="state">World state.</param>
    /// <param name="rules">Game rules (eye height).</param>
    /// <param name="player">The player.</param>
    /// <param name="entity">The persistent entity.</param>
    /// <param name="meters">The distance when the method returns true.</param>
    /// <returns>False when the player or the entity is unknown, or they are not on the same planet.</returns>
    public static bool TryDistanceToEntity(WorldState state, GameRules rules, PlayerId player, EntityId entity, out float meters)
    {
        meters = 0f;
        if (!state.Players.TryGet(player, out var playerState) || !TryGetEye(state, rules, playerState, out var playerPlanet, out var eye)
            || !state.PersistentEntities.TryGet(entity, out var persistent) || persistent.Frame.Kind != FrameKind.Planet
            || persistent.Frame.Id != playerPlanet.Value)
        {
            return false;
        }

        meters = Vector3.Distance(eye, persistent.Position);
        return true;
    }

    /// <summary>The centre of <paramref name="cell"/> in the frame of the planet the construct stands on (K6-32 D13); the same centre the reach measure above uses.</summary>
    /// <param name="state">World state.</param>
    /// <param name="construct">Construct holding the block.</param>
    /// <param name="cell">Cell of the block.</param>
    /// <param name="planet">The planet whose frame <paramref name="centre"/> is in.</param>
    /// <param name="centre">The centre when the method returns true.</param>
    /// <returns>False when the construct is unknown.</returns>
    public static bool TryCellCenter(WorldState state, EntityId construct, CellPos cell, out PlanetId planet, out Vector3 centre)
    {
        if (!state.Constructs.TryGet(construct, out _))
        {
            planet = default;
            centre = default;
            return false;
        }

        var local = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        return state.TryToPlanetFrame(FrameRef.Of(construct), local, out planet, out centre);
    }

    /// <summary>The eye of a player in the frame of their planet: the feet plus the local up times the eye height (the one formula of the validator and the server).</summary>
    internal static bool TryGetEye(WorldState state, GameRules rules, PlayerState player, out PlanetId planet, out Vector3 eye)
    {
        if (!state.TryToPlanetFrame(player.Frame, player.Position, out planet, out var feet))
        {
            eye = default;
            return false;
        }

        eye = feet + (TerrainMath.UpAt(feet) * rules.EyeHeight);
        return true;
    }
}
