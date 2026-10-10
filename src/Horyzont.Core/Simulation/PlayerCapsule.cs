using System.Numerics;

using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>Player capsule tests shared by intent validation and client previews.</summary>
public static class PlayerCapsule
{
    /// <summary>Segment of the capsule axis: the sphere centres at both ends.</summary>
    /// <param name="feet">Feet position in the planet frame.</param>
    /// <param name="rules">Game rules (capsule size).</param>
    /// <param name="bottom">Receives the lower sphere centre.</param>
    /// <param name="top">Receives the upper sphere centre.</param>
    public static void Axis(Vector3 feet, GameRules rules, out Vector3 bottom, out Vector3 top)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var up = TerrainMath.UpAt(feet);
        var r = rules.PlayerCapsuleRadius;
        bottom = feet + (up * r);
        top = feet + (up * MathF.Max(r, rules.PlayerCapsuleHeight - r));
    }

    /// <summary>True when a sphere intersects the capsule of a player.</summary>
    /// <param name="feet">Feet position in the planet frame.</param>
    /// <param name="center">Sphere centre in the planet frame.</param>
    /// <param name="radius">Sphere radius.</param>
    /// <param name="rules">Game rules (capsule size).</param>
    /// <returns>True when they intersect.</returns>
    public static bool IntersectsSphere(Vector3 feet, Vector3 center, float radius, GameRules rules)
    {
        Axis(feet, rules, out var bottom, out var top);
        return Geometry.DistanceToSegment(center, bottom, top) < rules.PlayerCapsuleRadius + radius;
    }

    /// <summary>True when the box [cell, cell + 1) in a construct pose intersects the capsule of a player.</summary>
    /// <param name="feet">Feet position in the planet frame.</param>
    /// <param name="origin">Corner of cell (0,0,0) in the planet frame.</param>
    /// <param name="rotation">Rotation from construct space to the planet frame.</param>
    /// <param name="cell">The cell.</param>
    /// <param name="rules">Game rules (capsule size).</param>
    /// <returns>True when they intersect.</returns>
    public static bool IntersectsCell(Vector3 feet, Vector3 origin, Quaternion rotation, CellPos cell, GameRules rules)
    {
        Axis(feet, rules, out var bottom, out var top);
        var inverse = Quaternion.Inverse(rotation);
        return IntersectsLocalCell(Vector3.Transform(bottom - origin, inverse), Vector3.Transform(top - origin, inverse), cell, rules);
    }

    /// <summary>The same test with the capsule axis already in construct space (nested constructs).</summary>
    internal static bool IntersectsLocalCell(Vector3 localBottom, Vector3 localTop, CellPos cell, GameRules rules)
    {
        var boxMin = new Vector3(cell.X, cell.Y, cell.Z);
        return Geometry.SegmentBoxDistance(localBottom, localTop, boxMin, boxMin + Vector3.One) < rules.PlayerCapsuleRadius;
    }
}
