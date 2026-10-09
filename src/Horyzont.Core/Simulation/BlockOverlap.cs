using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Exact overlap test of unit cells (K2-13 W2, gate decision D15): oriented boxes against oriented boxes by the separating axis
/// theorem, and the rule "a new cell does not overlap a cell of another construct" shared by the validator and the client previews.
/// Pure and allocation free.
/// </summary>
public static class BlockOverlap
{
    /// <summary>Penetration depth up to which two boxes only touch (face, edge or vertex contact is allowed).</summary>
    public const float TouchToleranceMeters = 1e-4f;

    /// <summary>Half extent of a unit cell.</summary>
    public const float CellHalfExtent = 0.5f;

    private const float ParallelAxisEpsilonSquared = 1e-6f;
    private const float CellBoundingRadius = 0.8660254f; // sqrt(3) / 2
    private const int MaxNestingDepth = 64;

    /// <summary>
    /// True when the boxes penetrate each other by more than <see cref="TouchToleranceMeters"/> along every one of the 15 axes of the
    /// separating axis test (3 + 3 face normals and 9 edge cross products; cross products close to zero are skipped).
    /// </summary>
    /// <param name="a">First box.</param>
    /// <param name="b">Second box.</param>
    /// <returns>True when they overlap.</returns>
    public static bool Intersects(in Obb a, in Obb b)
    {
        Span<Vector3> axes = stackalloc Vector3[6];
        axes[0] = Vector3.Transform(Vector3.UnitX, a.Rotation);
        axes[1] = Vector3.Transform(Vector3.UnitY, a.Rotation);
        axes[2] = Vector3.Transform(Vector3.UnitZ, a.Rotation);
        axes[3] = Vector3.Transform(Vector3.UnitX, b.Rotation);
        axes[4] = Vector3.Transform(Vector3.UnitY, b.Rotation);
        axes[5] = Vector3.Transform(Vector3.UnitZ, b.Rotation);
        var offset = b.Center - a.Center;
        for (var i = 0; i < 6; i++)
        {
            if (Separated(axes, a.HalfExtents, b.HalfExtents, offset, axes[i], axes[i].LengthSquared()))
            {
                return false;
            }
        }

        for (var i = 0; i < 3; i++)
        {
            for (var j = 3; j < 6; j++)
            {
                var axis = Vector3.Cross(axes[i], axes[j]);
                var lengthSquared = axis.LengthSquared();
                if (lengthSquared >= ParallelAxisEpsilonSquared && Separated(axes, a.HalfExtents, b.HalfExtents, offset, axis, lengthSquared))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The box of the cell [cell, cell + 1) of a construct pose.</summary>
    /// <param name="origin">Corner of cell (0,0,0) in the working frame.</param>
    /// <param name="rotation">Rotation from construct space to the working frame.</param>
    /// <param name="cell">The cell.</param>
    /// <returns>The box.</returns>
    public static Obb ForCell(Vector3 origin, Quaternion rotation, CellPos cell) =>
        new(
            origin + Vector3.Transform(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), rotation),
            rotation,
            new Vector3(CellHalfExtent));

    /// <summary>
    /// True when a new cell overlaps (deeper than <see cref="TouchToleranceMeters"/>) an occupied cell of a construct of the frame,
    /// directly or nested in other constructs of that frame; <paramref name="skip"/> is left out (the construct the block is added to).
    /// </summary>
    /// <param name="store">Constructs.</param>
    /// <param name="frame">Frame of <paramref name="center"/> and <paramref name="rotation"/> (the planet frame).</param>
    /// <param name="center">Centre of the new cell in <paramref name="frame"/>.</param>
    /// <param name="rotation">Rotation of the new cell in <paramref name="frame"/>.</param>
    /// <param name="skip">Construct to leave out, or <c>null</c> for none.</param>
    /// <returns>True when the new cell would share space with an existing block.</returns>
    public static bool OverlapsConstruct(ConstructStore store, FrameRef frame, Vector3 center, Quaternion rotation, EntityId? skip)
    {
        ArgumentNullException.ThrowIfNull(store);
        var all = store.All;
        for (var i = 0; i < all.Count; i++)
        {
            var construct = all[i];
            if (construct.BlockCount == 0 || construct.Id == skip
                || !TryGetPose(store, construct, frame, MaxNestingDepth, out var origin, out var poseRotation))
            {
                continue;
            }

            var inverse = Quaternion.Inverse(poseRotation);
            if (OverlapsOne(construct, Vector3.Transform(center - origin, inverse), inverse * rotation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool OverlapsOne(Construct construct, Vector3 center, Quaternion rotation)
    {
        // Bounding sphere of the new cell against the bounds box of the construct.
        var min = new Vector3(construct.BoundsMin.X, construct.BoundsMin.Y, construct.BoundsMin.Z);
        var max = new Vector3(construct.BoundsMax.X + 1, construct.BoundsMax.Y + 1, construct.BoundsMax.Z + 1);
        var outside = Vector3.Max(Vector3.Max(min - center, center - max), Vector3.Zero);
        if (outside.LengthSquared() > CellBoundingRadius * CellBoundingRadius)
        {
            return false;
        }

        // Cells in the axis-aligned box that encloses the new cell: at most 3 per axis.
        var ax = Vector3.Transform(Vector3.UnitX, rotation);
        var ay = Vector3.Transform(Vector3.UnitY, rotation);
        var az = Vector3.Transform(Vector3.UnitZ, rotation);
        var extent = new Vector3(
            CellHalfExtent * (MathF.Abs(ax.X) + MathF.Abs(ay.X) + MathF.Abs(az.X)),
            CellHalfExtent * (MathF.Abs(ax.Y) + MathF.Abs(ay.Y) + MathF.Abs(az.Y)),
            CellHalfExtent * (MathF.Abs(ax.Z) + MathF.Abs(ay.Z) + MathF.Abs(az.Z)));
        var from = center - extent;
        var to = center + extent;
        var x0 = Math.Max((int)MathF.Floor(from.X), construct.BoundsMin.X);
        var x1 = Math.Min((int)MathF.Floor(to.X), construct.BoundsMax.X);
        var y0 = Math.Max((int)MathF.Floor(from.Y), construct.BoundsMin.Y);
        var y1 = Math.Min((int)MathF.Floor(to.Y), construct.BoundsMax.Y);
        var z0 = Math.Max((int)MathF.Floor(from.Z), construct.BoundsMin.Z);
        var z1 = Math.Min((int)MathF.Floor(to.Z), construct.BoundsMax.Z);
        var candidate = new Obb(center, rotation, new Vector3(CellHalfExtent));
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                for (var z = z0; z <= z1; z++)
                {
                    var cell = new CellPos(x, y, z);
                    if (!construct.GetBlock(cell).IsEmpty
                        && Intersects(candidate, ForCell(Vector3.Zero, Quaternion.Identity, cell)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Pose of a construct in <paramref name="frame"/>, composed along its parent chain.</summary>
    /// <param name="store">Constructs.</param>
    /// <param name="construct">The construct.</param>
    /// <param name="frame">Target frame (the planet frame).</param>
    /// <param name="origin">Receives the corner of cell (0,0,0) in <paramref name="frame"/>.</param>
    /// <param name="rotation">Receives the rotation from construct space to <paramref name="frame"/>.</param>
    /// <returns>False when the parent chain does not reach <paramref name="frame"/>.</returns>
    public static bool TryGetConstructPose(ConstructStore store, Construct construct, FrameRef frame, out Vector3 origin, out Quaternion rotation)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(construct);
        return TryGetPose(store, construct, frame, MaxNestingDepth, out origin, out rotation);
    }

    private static bool TryGetPose(
        ConstructStore store, Construct construct, FrameRef frame, int depth, out Vector3 origin, out Quaternion rotation)
    {
        if (construct.Parent == frame)
        {
            origin = construct.Origin;
            rotation = construct.Rotation;
            return true;
        }

        if (depth > 0
            && construct.Parent.Kind == FrameKind.Construct
            && store.TryGet(new EntityId(construct.Parent.Id), out var parent)
            && TryGetPose(store, parent, frame, depth - 1, out var parentOrigin, out var parentRotation))
        {
            origin = parentOrigin + Vector3.Transform(construct.Origin, parentRotation);
            rotation = parentRotation * construct.Rotation;
            return true;
        }

        origin = default;
        rotation = Quaternion.Identity;
        return false;
    }

    /// <summary>True when the projections of both boxes on an axis are separated or only touch (overlap up to the tolerance).</summary>
    private static bool Separated(
        ReadOnlySpan<Vector3> axes, Vector3 halfA, Vector3 halfB, Vector3 offset, Vector3 axis, float axisLengthSquared)
    {
        var ra = (halfA.X * MathF.Abs(Vector3.Dot(axes[0], axis))) + (halfA.Y * MathF.Abs(Vector3.Dot(axes[1], axis)))
            + (halfA.Z * MathF.Abs(Vector3.Dot(axes[2], axis)));
        var rb = (halfB.X * MathF.Abs(Vector3.Dot(axes[3], axis))) + (halfB.Y * MathF.Abs(Vector3.Dot(axes[4], axis)))
            + (halfB.Z * MathF.Abs(Vector3.Dot(axes[5], axis)));
        var overlap = (ra + rb - MathF.Abs(Vector3.Dot(offset, axis))) / MathF.Sqrt(axisLengthSquared);
        return overlap <= TouchToleranceMeters;
    }
}
