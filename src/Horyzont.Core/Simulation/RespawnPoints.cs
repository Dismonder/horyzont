using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;

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
    // A building tilted more than 120 degrees from the planet up is upside down; a side is dropped when it points more than 45 degrees below the horizon.
    private const double MinUprightDot = -0.5;
    private static readonly double MaxDownDot = Math.Sqrt(0.5);

    // The sides of the bed cell in the axes of the construct, in the fixed order of the candidates: +X, -X, +Z, -Z.
    private static readonly Vector3D[] _sides = [new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)];

    /// <summary>
    /// The places next to the bed of the player, in a fixed order: the sides +X, -X, +Z, -Z of its cell in the axes of the construct, converted to the planet frame in
    /// double precision. Empty when the player has no bed, the bed is not valid (type, state, owner), the construct is not on a planet, a ship is not parked, or the
    /// construct is upside down; a side that points down is left out.
    /// </summary>
    /// <param name="state">World state.</param>
    /// <param name="player">The player.</param>
    /// <param name="candidates">Receives the candidates (cleared first).</param>
    /// <returns>True when there is at least one candidate.</returns>
    public static bool TryGetBedCandidates(WorldState state, PlayerId player, List<RespawnPose> candidates)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(candidates);
        candidates.Clear();
        if (!state.Progress.TryGet(player, out var progress) || progress.RespawnBed is not { } bed
            || !bed.Cell.IsInBounds || !state.Constructs.TryGet(bed.Construct, out var construct))
        {
            return false;
        }

        // The block, its state and its owner: a bed that was replaced, taken down or given to someone else is no respawn point (the reference is not cleared here).
        var block = construct.GetBlock(bed.Cell);
        if (block.IsEmpty || block.TypeId != bed.ExpectedType
            || !state.BlockEntities.TryGet(new BlockRef(bed.Construct, bed.Cell), out var entity) || entity is not BedBlockState { } bedState || bedState.Owner != player)
        {
            return false;
        }

        // A building on a planet, or a ship that stands there: not a ship in flight, not a body of the star system (D15).
        if (construct.Parent.Kind != FrameKind.Planet || !state.Planets.TryGet(new PlanetId(construct.Parent.Id), out _))
        {
            return false;
        }

        if (construct.Mode == ConstructMode.Ship && !(state.ShipStates.TryGet(construct.Id, out var ship) && (ship.Flags & ShipStateFlags.Parked) != 0))
        {
            return false;
        }

        var planet = new PlanetId(construct.Parent.Id);
        var pose = FrameMath.FromFloat(construct.Origin, construct.Rotation);
        var centre = FrameMath.TransformPoint(pose, new Vector3D(bed.Cell.X + 0.5, bed.Cell.Y + 0.5, bed.Cell.Z + 0.5));

        // The planet up is radial, from the centre of the planet; the Y axis of a tilted building is not "up" (K6-33). An upside-down building has no place to stand.
        var up = Vector3D.Normalize(centre);
        var constructUp = FrameMath.TransformDirection(pose, new Vector3D(0, 1, 0));
        if (up == Vector3D.Zero || Vector3D.Dot(constructUp, up) < MinUprightDot)
        {
            return false;
        }

        for (var i = 0; i < _sides.Length; i++)
        {
            var side = _sides[i];

            // A side that points more than 45 degrees below the horizon is not a place to stand, whatever lies there.
            if (Vector3D.Dot(FrameMath.TransformDirection(pose, side), up) < -MaxDownDot)
            {
                continue;
            }

            var feet = FrameMath.TransformPoint(pose, new Vector3D(bed.Cell.X + 0.5 + side.X, bed.Cell.Y, bed.Cell.Z + 0.5 + side.Z));
            if (FrameMath.TryVectorToFloat(feet, out var position))
            {
                candidates.Add(new RespawnPose(planet, position, Quaternion.Identity));
            }
        }

        return candidates.Count > 0;
    }

    /// <summary>Chooses the first safe candidate of the bed, otherwise the start of the world.</summary>
    /// <param name="context">The world and the probe results.</param>
    /// <returns>The chosen point.</returns>
    /// <exception cref="ArgumentException">The start of the world is not on the home planet.</exception>
    public static RespawnPoint Resolve(RespawnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.WorldStart.Planet != context.State.HomePlanet)
        {
            throw new ArgumentException("The start of the world must be on the home planet.", nameof(context));
        }

        var safe = context.CandidateSafe;
        var candidates = new List<RespawnPose>(_sides.Length);
        if (safe is not null && TryGetBedCandidates(context.State, context.Player, candidates))
        {
            for (var i = 0; i < candidates.Count && i < safe.Count; i++)
            {
                if (safe[i])
                {
                    return new RespawnPoint(RespawnSource.Bed, candidates[i]);
                }
            }
        }

        return new RespawnPoint(RespawnSource.WorldStart, context.WorldStart);
    }
}
