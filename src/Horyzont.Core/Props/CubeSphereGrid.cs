using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Props;

/// <summary>
/// The equal-angle cube-sphere grid of K6-31 D4: six faces (0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z) of N x N cells, N = 8 * max(1, round(R * pi / 2 / 64 m)).
/// Directions are unit vectors in the planet frame. The tangent axes of a face cycle without mirroring (X: U = Y, V = Z; Y: U = Z, V = X;
/// Z: U = X, V = Y); a cell column c maps to alpha = ((c + 0.5) / N - 0.5) * pi / 2 and the direction is normalize(sign * axis + tan(alpha_u) * U +
/// tan(alpha_v) * V), all in double. The grid only maps cells to directions; which props exist is decided by the identifier alone (frozen by A1).
/// </summary>
public static class CubeSphereGrid
{
    private const int CellsPerRegionEdge = 8;
    private const double MetersPerRegion = 64.0;
    private static readonly PropIdComparer _idOrder = new();

    /// <summary>Cells per face edge for a planet radius.</summary>
    /// <param name="planetRadiusMeters">Radius in metres.</param>
    /// <returns>A multiple of 8, at least 8.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite or the grid would not fit a <see cref="PropId"/>.</exception>
    public static int CellsPerFace(float planetRadiusMeters)
    {
        if (!float.IsFinite(planetRadiusMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "The radius must be finite.");
        }

        var regions = Math.Round(planetRadiusMeters * Math.PI / 2 / MetersPerRegion, MidpointRounding.AwayFromZero);
        if (regions > PropId.MaxCellsPerFace / CellsPerRegionEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "The planet is too large for a prop grid.");
        }

        return CellsPerRegionEdge * Math.Max(1, (int)regions);
    }

    /// <summary>Unit direction to the centre of a cell.</summary>
    /// <param name="face">Face 0-5.</param>
    /// <param name="cellU">Cell column.</param>
    /// <param name="cellV">Cell row.</param>
    /// <param name="cellsPerFace">Cells per face edge.</param>
    /// <returns>The direction.</returns>
    public static Vector3 DirectionOfCell(int face, int cellU, int cellV, int cellsPerFace)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(face, 5);
        ArgumentOutOfRangeException.ThrowIfNegative(cellU);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cellU, cellsPerFace);
        ArgumentOutOfRangeException.ThrowIfNegative(cellV);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cellV, cellsPerFace);

        var axis = face / 2;
        Span<double> parts = stackalloc double[3];
        parts[axis] = (face & 1) == 0 ? 1.0 : -1.0;
        parts[(axis + 1) % 3] = Math.Tan(((((double)cellU + 0.5) / cellsPerFace) - 0.5) * (Math.PI / 2));
        parts[(axis + 2) % 3] = Math.Tan(((((double)cellV + 0.5) / cellsPerFace) - 0.5) * (Math.PI / 2));
        var length = Math.Sqrt((parts[0] * parts[0]) + (parts[1] * parts[1]) + (parts[2] * parts[2]));
        return new Vector3((float)(parts[0] / length), (float)(parts[1] / length), (float)(parts[2] / length));
    }

    /// <summary>The cell a direction falls into; a direction on a seam goes to exactly one cell (the lower axis wins a tie).</summary>
    /// <param name="direction">Direction (any length above zero).</param>
    /// <param name="cellsPerFace">Cells per face edge.</param>
    /// <param name="face">Face of the cell.</param>
    /// <param name="cellU">Column of the cell.</param>
    /// <param name="cellV">Row of the cell.</param>
    public static void CellOfDirection(Vector3 direction, int cellsPerFace, out int face, out int cellU, out int cellV)
    {
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The direction must be finite.");
        }

        var x = (double)direction.X;
        var y = (double)direction.Y;
        var z = (double)direction.Z;
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        var az = Math.Abs(z);
        var axis = 0;
        var major = ax;
        if (ay > major)
        {
            axis = 1;
            major = ay;
        }

        if (az > major)
        {
            axis = 2;
            major = az;
        }

        if (major == 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The direction must not be zero.");
        }

        var positive = Component(x, y, z, axis) > 0.0;
        face = (axis * 2) + (positive ? 0 : 1);
        cellU = CellOfTangent(Component(x, y, z, (axis + 1) % 3) / major, cellsPerFace);
        cellV = CellOfTangent(Component(x, y, z, (axis + 2) % 3) / major, cellsPerFace);
    }

    /// <summary>Appends the slot 0 identifier of every cell that a sphere may touch, in ascending identifier order, without duplicates.</summary>
    /// <param name="center">Centre of the sphere in the planet frame, metres.</param>
    /// <param name="sphereRadiusMeters">Radius of the sphere, metres.</param>
    /// <param name="planetRadiusMeters">Radius of the planet, metres (gives the grid size and the metres per cell).</param>
    /// <param name="cells">Receives the identifiers; not cleared.</param>
    public static void CellsInSphere(Vector3 center, float sphereRadiusMeters, float planetRadiusMeters, List<PropId> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var cellsPerFace = CellsPerFace(planetRadiusMeters);
        var start = cells.Count;
        var cx = (double)center.X;
        var cy = (double)center.Y;
        var cz = (double)center.Z;
        var distance = Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz));
        var cellMeters = planetRadiusMeters * Math.PI / 2 / cellsPerFace;
        var reach = sphereRadiusMeters + (0.5 * cellMeters);

        if (distance <= reach)
        {
            // The sphere reaches the centre of the planet: every cell whose centre lies within the reach of the sphere.
            AppendAllNear(cx, cy, cz, reach, planetRadiusMeters, cellsPerFace, cells);
        }
        else
        {
            AppendTangentSamples(cx, cy, cz, distance, reach, cellMeters, cellsPerFace, cells);
        }

        cells.Sort(start, cells.Count - start, _idOrder);
        var write = start;
        for (var read = start; read < cells.Count; read++)
        {
            if (write == start || cells[read] != cells[write - 1])
            {
                cells[write++] = cells[read];
            }
        }

        cells.RemoveRange(write, cells.Count - write);
    }

    private static double Component(double x, double y, double z, int index) => index == 0 ? x : index == 1 ? y : z;

    private static int CellOfTangent(double tangentRatio, int cellsPerFace)
    {
        // The arctangent of the ratio is the equal-angle coordinate in -pi/4 .. pi/4.
        var cell = (int)Math.Floor((((Math.Atan(tangentRatio) * 4.0 / Math.PI) + 1.0) / 2.0) * cellsPerFace);
        return Math.Clamp(cell, 0, cellsPerFace - 1);
    }

    private static void AppendAllNear(double cx, double cy, double cz, double reach, float planetRadiusMeters, int cellsPerFace, List<PropId> cells)
    {
        var reachSquared = reach * reach;
        for (var face = 0; face < 6; face++)
        {
            for (var u = 0; u < cellsPerFace; u++)
            {
                for (var v = 0; v < cellsPerFace; v++)
                {
                    var direction = DirectionOfCell(face, u, v, cellsPerFace);
                    var dx = (direction.X * (double)planetRadiusMeters) - cx;
                    var dy = (direction.Y * (double)planetRadiusMeters) - cy;
                    var dz = (direction.Z * (double)planetRadiusMeters) - cz;
                    if ((dx * dx) + (dy * dy) + (dz * dz) <= reachSquared)
                    {
                        cells.Add(PropId.Create(face, u, v, 0));
                    }
                }
            }
        }
    }

    private static void AppendTangentSamples(double cx, double cy, double cz, double distance, double reach, double cellMeters, int cellsPerFace, List<PropId> cells)
    {
        var nx = cx / distance;
        var ny = cy / distance;
        var nz = cz / distance;

        // Tangent basis from the axis least aligned with the direction of the centre.
        double ex = 0, ey = 0, ez = 0;
        if (Math.Abs(nx) <= Math.Abs(ny) && Math.Abs(nx) <= Math.Abs(nz))
        {
            ex = 1;
        }
        else if (Math.Abs(ny) <= Math.Abs(nz))
        {
            ey = 1;
        }
        else
        {
            ez = 1;
        }

        var t1x = (ny * ez) - (nz * ey);
        var t1y = (nz * ex) - (nx * ez);
        var t1z = (nx * ey) - (ny * ex);
        var t1Length = Math.Sqrt((t1x * t1x) + (t1y * t1y) + (t1z * t1z));
        t1x /= t1Length;
        t1y /= t1Length;
        t1z /= t1Length;
        var t2x = (ny * t1z) - (nz * t1y);
        var t2y = (nz * t1x) - (nx * t1z);
        var t2z = (nx * t1y) - (ny * t1x);

        // The samples are denser than the smallest cell edge (equal-angle cells shrink by up to a third towards the face corners), so every cell
        // that the disc of the reach covers by more than a sliver holds a sample.
        var step = 0.35 * cellMeters;
        var count = (int)Math.Ceiling(reach / step);
        var reachSquared = reach * reach;
        for (var i = -count; i <= count; i++)
        {
            for (var j = -count; j <= count; j++)
            {
                var a = i * step;
                var b = j * step;
                if ((a * a) + (b * b) > reachSquared)
                {
                    continue;
                }

                var px = cx + (a * t1x) + (b * t2x);
                var py = cy + (a * t1y) + (b * t2y);
                var pz = cz + (a * t1z) + (b * t2z);
                CellOfDirection(new Vector3((float)px, (float)py, (float)pz), cellsPerFace, out var face, out var u, out var v);
                cells.Add(PropId.Create(face, u, v, 0));
            }
        }
    }

    private sealed class PropIdComparer : IComparer<PropId>
    {
        public int Compare(PropId x, PropId y) => x.Value.CompareTo(y.Value);
    }
}
