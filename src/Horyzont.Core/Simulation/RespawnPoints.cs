using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Simulation;

/// <summary>Where a respawn comes from.</summary>
public enum RespawnSource : byte
{
    /// <summary>Next to the own bed.</summary>
    Bed = 1,

    /// <summary>The start of the world on the home planet.</summary>
    WorldStart = 2,
}

/// <summary>A pose in the frame of a planet: the feet of a player and the rotation of the body.</summary>
/// <param name="Planet">The planet whose frame the values are in.</param>
/// <param name="Position">Feet.</param>
/// <param name="Rotation">Body rotation.</param>
public readonly record struct RespawnPose(PlanetId Planet, Vector3 Position, Quaternion Rotation);

/// <summary>The result of <see cref="RespawnPoints.Resolve"/>.</summary>
/// <param name="Source">Whether the bed or the start of the world was chosen.</param>
/// <param name="Pose">The pose.</param>
public readonly record struct RespawnPoint(RespawnSource Source, RespawnPose Pose);

/// <summary>
/// What <see cref="RespawnPoints.Resolve"/> needs besides the world: which of the candidates of the bed a probe of the terrain found safe, and the safe pose of the
/// world start on the home planet. The probe belongs to the adapter; Core asks neither Godot nor the generator nor the clock (K6-35 D14).
/// </summary>
/// <param name="State">World state.</param>
/// <param name="Player">The player who respawns.</param>
/// <param name="CandidateSafe">One flag per candidate of <see cref="RespawnPoints.TryGetBedCandidates"/>, in its order; null or too short counts as not safe.</param>
/// <param name="WorldStart">The safe start of the world, on <see cref="WorldState.HomePlanet"/>.</param>
public sealed record RespawnContext(WorldState State, PlayerId Player, IReadOnlyList<bool>? CandidateSafe, RespawnPose WorldStart);

/// <summary>
/// The one function that chooses where a player appears, for a death and for leaving a seat (K6-35 D14; K5-25 uses it without the reset of a death):
/// next to the own valid bed, otherwise at the start of the world.
/// </summary>
public static class RespawnPoints
{
    /// <summary>
    /// The places next to the bed of the player, in a fixed order: the sides +X, -X, +Z, -Z of its cell in the axes of the construct, converted to the planet frame in
    /// double precision. Empty when the player has no bed, the bed is not valid (type, state, owner), the construct is not on a planet, a ship is not parked, or the
    /// construct is upside down; a side that points down is left out.
    /// </summary>
    /// <param name="state">World state.</param>
    /// <param name="player">The player.</param>
    /// <param name="candidates">Receives the candidates (cleared first).</param>
    /// <returns>True when there is at least one candidate.</returns>
    public static bool TryGetBedCandidates(WorldState state, PlayerId player, List<RespawnPose> candidates) => throw new NotImplementedException();

    /// <summary>Chooses the first safe candidate of the bed, otherwise the start of the world.</summary>
    /// <param name="context">The world and the probe results.</param>
    /// <returns>The chosen point.</returns>
    public static RespawnPoint Resolve(RespawnContext context) => throw new NotImplementedException();
}
