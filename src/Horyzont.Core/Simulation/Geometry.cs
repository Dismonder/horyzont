using System.Numerics;

using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>Deterministic float geometry used by validation.</summary>
internal static class Geometry
{
    internal const float DirectionTolerance = 0.01f;
    internal const float RotationTolerance = 1e-3f;

    internal static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    internal static bool IsFinite(Quaternion q) =>
        float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W);

    internal static bool IsDirection(Vector3 v) => IsFinite(v) && MathF.Abs(v.Length() - 1f) <= DirectionTolerance;

    internal static bool IsRotation(Quaternion q) => IsFinite(q) && MathF.Abs(q.Length() - 1f) <= RotationTolerance;

    /// <summary>Segment of the capsule axis: the sphere centres at both ends.</summary>
    internal static void CapsuleAxis(Vector3 feet, GameRules rules, out Vector3 bottom, out Vector3 top)
    {
        var up = TerrainMath.UpAt(feet);
        var r = rules.PlayerCapsuleRadius;
        bottom = feet + (up * r);
        top = feet + (up * MathF.Max(r, rules.PlayerCapsuleHeight - r));
    }

    internal static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared > 0 ? Math.Clamp(Vector3.Dot(point - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return Vector3.Distance(point, a + (ab * t));
    }

    /// <summary>Distance between a segment and an axis-aligned box (ternary search on a convex function).</summary>
    internal static float SegmentBoxDistance(Vector3 a, Vector3 b, Vector3 boxMin, Vector3 boxMax)
    {
        float low = 0, high = 1;
        for (var i = 0; i < 48; i++)
        {
            var m1 = low + ((high - low) / 3f);
            var m2 = high - ((high - low) / 3f);
            if (PointBoxDistanceSquared(Vector3.Lerp(a, b, m1), boxMin, boxMax) <= PointBoxDistanceSquared(Vector3.Lerp(a, b, m2), boxMin, boxMax))
            {
                high = m2;
            }
            else
            {
                low = m1;
            }
        }

        return MathF.Sqrt(PointBoxDistanceSquared(Vector3.Lerp(a, b, (low + high) * 0.5f), boxMin, boxMax));
    }

    internal static Vector3 ProjectOnPlane(Vector3 v, Vector3 normal) => v - (normal * Vector3.Dot(v, normal));

    private static float PointBoxDistanceSquared(Vector3 p, Vector3 boxMin, Vector3 boxMax)
    {
        var d = Vector3.Max(Vector3.Max(boxMin - p, p - boxMax), Vector3.Zero);
        return d.LengthSquared();
    }
}
