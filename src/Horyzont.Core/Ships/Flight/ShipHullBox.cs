using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Ships.Flight;

/// <summary>An axis-aligned box of whole cells in construct space: cells from <paramref name="Min"/> to <paramref name="Max"/> − 1.</summary>
/// <param name="Min">Smallest cell (inclusive).</param>
/// <param name="Max">Largest corner (exclusive): the box spans Max − Min cells along each axis.</param>
public readonly record struct ShipHullBox(CellPos Min, CellPos Max)
{
    /// <summary>Gets the number of cells of the box.</summary>
    public long Volume => (long)(Max.X - Min.X) * (Max.Y - Min.Y) * (Max.Z - Min.Z);

    /// <summary>True when the box contains a cell.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>Whether the cell is inside.</returns>
    public bool Contains(CellPos cell) =>
        cell.X >= Min.X && cell.X < Max.X && cell.Y >= Min.Y && cell.Y < Max.Y && cell.Z >= Min.Z && cell.Z < Max.Z;
}
