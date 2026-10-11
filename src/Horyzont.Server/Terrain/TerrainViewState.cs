using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Server.Terrain;

/// <summary>A validated viewer report of one client.</summary>
/// <param name="Planet">Planet.</param>
/// <param name="VisualPosition">Visual viewer, planet frame.</param>
/// <param name="VisualDistance">Visual distance in metres.</param>
/// <param name="CollisionPosition">Collision viewer, planet frame.</param>
/// <param name="CollisionDistance">Collision distance in metres.</param>
internal readonly record struct TerrainViewState(
    PlanetId Planet, Vector3 VisualPosition, float VisualDistance, Vector3 CollisionPosition, float CollisionDistance)
{
    /// <summary>Half diagonal of a block of 16 voxels of 1 m, rounded up: a block counts when any part may be in range.</summary>
    private const float BlockMargin = 14f;

    /// <summary>Tests a block centre against both viewers.</summary>
    /// <param name="center">Block centre, planet frame.</param>
    /// <param name="distanceSquared">Squared distance to the nearer viewer.</param>
    /// <param name="near">Whether the block is within the live radius of the collision viewer.</param>
    /// <returns>Whether the block is inside the visual or the collision range.</returns>
    public bool Covers(Vector3 center, out float distanceSquared, out bool near)
    {
        var visual = Vector3.DistanceSquared(center, VisualPosition);
        var collision = Vector3.DistanceSquared(center, CollisionPosition);
        distanceSquared = MathF.Min(visual, collision);
        var live = TerrainReplicationPolicy.LiveRadius + BlockMargin;
        near = collision <= live * live;
        var visualReach = VisualDistance + BlockMargin;
        var collisionReach = CollisionDistance + BlockMargin;
        return visual <= visualReach * visualReach || collision <= collisionReach * collisionReach;
    }

    /// <summary>Whether the interest of the client changed enough to look for blocks it lacks: another block or distance.</summary>
    /// <param name="other">The earlier state.</param>
    /// <returns>True when the sets of covered blocks differ.</returns>
    public bool InterestDiffers(in TerrainViewState other) =>
        Planet != other.Planet || VisualDistance != other.VisualDistance || CollisionDistance != other.CollisionDistance
        || BlockOf(VisualPosition) != BlockOf(other.VisualPosition) || BlockOf(CollisionPosition) != BlockOf(other.CollisionPosition);

    private static (int X, int Y, int Z) BlockOf(Vector3 p) =>
        ((int)MathF.Floor(p.X / 16f), (int)MathF.Floor(p.Y / 16f), (int)MathF.Floor(p.Z / 16f));
}
