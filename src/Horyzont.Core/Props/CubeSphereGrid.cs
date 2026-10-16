using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Props;

/// <summary>
/// The equal-angle cube-sphere grid of K6-31 D4: six faces (0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z) of N x N cells, N = 8 * max(1, round(R * pi / 2 / 64 m)).
/// Directions are unit vectors in the planet frame.
/// </summary>
public static class CubeSphereGrid
{
    /// <summary>Cells per face edge for a planet radius.</summary>
    /// <param name="planetRadiusMeters">Radius in metres.</param>
    /// <returns>A multiple of 8, at least 8.</returns>
    public static int CellsPerFace(float planetRadiusMeters)
    {
        // K6-31 A2.
        _ = planetRadiusMeters;
        return 0;
    }

    /// <summary>Unit direction to the centre of a cell.</summary>
    /// <param name="face">Face 0-5.</param>
    /// <param name="cellU">Cell column.</param>
    /// <param name="cellV">Cell row.</param>
    /// <param name="cellsPerFace">Cells per face edge.</param>
    /// <returns>The direction.</returns>
    public static Vector3 DirectionOfCell(int face, int cellU, int cellV, int cellsPerFace)
    {
        // K6-31 A2.
        _ = (face, cellU, cellV, cellsPerFace);
        return Vector3.Zero;
    }

    /// <summary>The cell a direction falls into; a direction on a seam goes to exactly one cell.</summary>
    /// <param name="direction">Direction (any length above zero).</param>
    /// <param name="cellsPerFace">Cells per face edge.</param>
    /// <param name="face">Face of the cell.</param>
    /// <param name="cellU">Column of the cell.</param>
    /// <param name="cellV">Row of the cell.</param>
    public static void CellOfDirection(Vector3 direction, int cellsPerFace, out int face, out int cellU, out int cellV)
    {
        // K6-31 A2.
        _ = (direction, cellsPerFace);
        face = -1;
        cellU = -1;
        cellV = -1;
    }

    /// <summary>Appends the slot 0 identifier of every cell that a sphere may touch, in ascending identifier order.</summary>
    /// <param name="center">Centre of the sphere in the planet frame, metres.</param>
    /// <param name="sphereRadiusMeters">Radius of the sphere, metres.</param>
    /// <param name="planetRadiusMeters">Radius of the planet, metres (gives the grid size and the metres per cell).</param>
    /// <param name="cells">Receives the identifiers; not cleared.</param>
    public static void CellsInSphere(Vector3 center, float sphereRadiusMeters, float planetRadiusMeters, List<PropId> cells)
    {
        // K6-31 A2.
        ArgumentNullException.ThrowIfNull(cells);
        _ = (center, sphereRadiusMeters, planetRadiusMeters);
    }
}
