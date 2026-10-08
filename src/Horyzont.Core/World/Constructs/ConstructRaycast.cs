using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Constructs;

/// <summary>Ray against constructs of one frame (Amanatides–Woo in each construct's space). Does not allocate.</summary>
public static class ConstructRaycast
{
    /// <summary>
    /// Finds the nearest occupied cell hit by a ray among constructs placed in <paramref name="frame"/>, directly or nested in
    /// other constructs of that frame.
    /// </summary>
    /// <param name="store">Constructs.</param>
    /// <param name="frame">Frame of the ray.</param>
    /// <param name="origin">Ray origin in <paramref name="frame"/>.</param>
    /// <param name="direction">Normalized ray direction in <paramref name="frame"/>.</param>
    /// <param name="maxDistance">Maximum distance in metres.</param>
    /// <param name="hit">The nearest hit; ties go to the lower construct id.</param>
    /// <returns>True when a cell was hit.</returns>
    public static bool Raycast(
        ConstructStore store, FrameRef frame, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit)
    {
        hit = default;
        var found = false;
        var best = maxDistance;
        var all = store.All;
        for (var i = 0; i < all.Count; i++)
        {
            var construct = all[i];
            if (construct.BlockCount == 0
                || !TryToParentSpace(store, construct, frame, origin, direction, all.Count, out var o, out var d))
            {
                continue;
            }

            // Frame changes are rigid, so distances along the ray stay the same in the construct's parent space.
            if (RaycastOne(construct, o, d, best, out var cell, out var face, out var distance)
                && (!found || distance < best))
            {
                found = true;
                best = distance;
                hit = new ConstructHit(construct.Id, cell, face, origin + (direction * distance), distance);
            }
        }

        return found;
    }

    /// <summary>
    /// Moves a ray from <paramref name="frame"/> into the parent space of <paramref name="construct"/> along its parent
    /// chain. False when the chain does not lead to <paramref name="frame"/> within <paramref name="depth"/> steps.
    /// </summary>
    private static bool TryToParentSpace(
        ConstructStore store, Construct construct, FrameRef frame, Vector3 origin, Vector3 direction, int depth, out Vector3 o, out Vector3 d)
    {
        o = origin;
        d = direction;
        if (construct.Parent == frame)
        {
            return true;
        }

        if (depth <= 0
            || construct.Parent.Kind != FrameKind.Construct
            || !store.TryGet(new EntityId(construct.Parent.Id), out var parent)
            || !TryToParentSpace(store, parent, frame, origin, direction, depth - 1, out o, out d))
        {
            return false;
        }

        o = parent.ParentToLocal(o);
        d = Vector3.Transform(d, Quaternion.Inverse(parent.Rotation));
        return true;
    }

    /// <summary>Ray against one construct, in the parent frame of the construct.</summary>
    internal static bool RaycastOne(
        Construct construct, Vector3 origin, Vector3 direction, float maxDistance, out CellPos cell, out Face face, out float distance)
    {
        cell = default;
        face = default;
        distance = 0;
        var inverse = Quaternion.Inverse(construct.Rotation);
        var o = Vector3.Transform(origin - construct.Origin, inverse);
        var d = Vector3.Transform(direction, inverse);
        var min = new Vector3(construct.BoundsMin.X, construct.BoundsMin.Y, construct.BoundsMin.Z);
        var max = new Vector3(construct.BoundsMax.X + 1, construct.BoundsMax.Y + 1, construct.BoundsMax.Z + 1);

        // Bounding sphere rejection.
        var center = (min + max) * 0.5f;
        var radius = (max - min).Length() * 0.5f;
        var toCenter = center - o;
        var along = Vector3.Dot(toCenter, d);
        if (toCenter.LengthSquared() - (along * along) > radius * radius || along + radius < 0 || along - radius > maxDistance)
        {
            return false;
        }

        // Slab intersection with the bounds box.
        float tEnter = 0, tExit = maxDistance;
        var enterAxis = -1;
        if (!Slab(o.X, d.X, min.X, max.X, 0, ref tEnter, ref tExit, ref enterAxis)
            || !Slab(o.Y, d.Y, min.Y, max.Y, 1, ref tEnter, ref tExit, ref enterAxis)
            || !Slab(o.Z, d.Z, min.Z, max.Z, 2, ref tEnter, ref tExit, ref enterAxis))
        {
            return false;
        }

        var start = o + (d * tEnter);
        var x = Clamp((int)MathF.Floor(start.X), construct.BoundsMin.X, construct.BoundsMax.X);
        var y = Clamp((int)MathF.Floor(start.Y), construct.BoundsMin.Y, construct.BoundsMax.Y);
        var z = Clamp((int)MathF.Floor(start.Z), construct.BoundsMin.Z, construct.BoundsMax.Z);
        switch (enterAxis)
        {
            case 0:
                x = d.X > 0 ? construct.BoundsMin.X : construct.BoundsMax.X;
                break;
            case 1:
                y = d.Y > 0 ? construct.BoundsMin.Y : construct.BoundsMax.Y;
                break;
            case 2:
                z = d.Z > 0 ? construct.BoundsMin.Z : construct.BoundsMax.Z;
                break;
            default:
                // The ray starts inside the bounds: report the face facing the dominant direction.
                enterAxis = DominantAxis(d);
                break;
        }

        var currentFace = EntryFace(enterAxis, d);
        int stepX = Math.Sign(d.X), stepY = Math.Sign(d.Y), stepZ = Math.Sign(d.Z);
        var tMaxX = NextBoundary(x, o.X, d.X);
        var tMaxY = NextBoundary(y, o.Y, d.Y);
        var tMaxZ = NextBoundary(z, o.Z, d.Z);
        var tDeltaX = d.X != 0 ? MathF.Abs(1f / d.X) : float.PositiveInfinity;
        var tDeltaY = d.Y != 0 ? MathF.Abs(1f / d.Y) : float.PositiveInfinity;
        var tDeltaZ = d.Z != 0 ? MathF.Abs(1f / d.Z) : float.PositiveInfinity;
        var t = tEnter;
        var limit = MathF.Min(tExit, maxDistance);
        while (true)
        {
            if (x < construct.BoundsMin.X || x > construct.BoundsMax.X
                || y < construct.BoundsMin.Y || y > construct.BoundsMax.Y
                || z < construct.BoundsMin.Z || z > construct.BoundsMax.Z)
            {
                return false;
            }

            var candidate = new CellPos(x, y, z);
            if (!construct.GetBlock(candidate).IsEmpty)
            {
                cell = candidate;
                face = currentFace;
                distance = t;
                return true;
            }

            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                t = tMaxX;
                x += stepX;
                tMaxX += tDeltaX;
                currentFace = stepX > 0 ? Face.NegX : Face.PosX;
            }
            else if (tMaxY <= tMaxZ)
            {
                t = tMaxY;
                y += stepY;
                tMaxY += tDeltaY;
                currentFace = stepY > 0 ? Face.NegY : Face.PosY;
            }
            else
            {
                t = tMaxZ;
                z += stepZ;
                tMaxZ += tDeltaZ;
                currentFace = stepZ > 0 ? Face.NegZ : Face.PosZ;
            }

            if (t > limit)
            {
                return false;
            }
        }
    }

    private static bool Slab(float o, float d, float min, float max, int axis, ref float tEnter, ref float tExit, ref int enterAxis)
    {
        if (d == 0)
        {
            return o >= min && o <= max;
        }

        var t1 = (min - o) / d;
        var t2 = (max - o) / d;
        var near = MathF.Min(t1, t2);
        var far = MathF.Max(t1, t2);
        if (near > tEnter)
        {
            tEnter = near;
            enterAxis = axis;
        }

        tExit = MathF.Min(tExit, far);
        return tEnter <= tExit;
    }

    private static float NextBoundary(int cell, float o, float d)
    {
        if (d > 0)
        {
            return (cell + 1 - o) / d;
        }

        return d < 0 ? (cell - o) / d : float.PositiveInfinity;
    }

    private static int DominantAxis(Vector3 d)
    {
        float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y), az = MathF.Abs(d.Z);
        if (ax >= ay && ax >= az)
        {
            return 0;
        }

        return ay >= az ? 1 : 2;
    }

    private static Face EntryFace(int axis, Vector3 d) => axis switch
    {
        0 => d.X > 0 ? Face.NegX : Face.PosX,
        1 => d.Y > 0 ? Face.NegY : Face.PosY,
        _ => d.Z > 0 ? Face.NegZ : Face.PosZ,
    };

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
