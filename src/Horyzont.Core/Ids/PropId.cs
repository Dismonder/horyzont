namespace Horyzont.Core.Ids;

/// <summary>
/// Identifier of a generated prop (K6-31 D4): the position in the cube-sphere grid of the planet, independent of the terrain. Bits 63-61 are
/// reserved (0), 60-58 face 0-5, 57-41 region U, 40-24 region V, 23-21 cell U in the region, 20-18 cell V in the region, 17-16 slot 0-3, 15-0
/// reserved (0). K6-31 A2 adds the behaviour; the layout is frozen by the A1 review.
/// </summary>
/// <param name="Value">The packed value.</param>
public readonly record struct PropId(ulong Value)
{
    /// <summary>Most cells per face edge an identifier can hold (17 bits of region times 8 cells).</summary>
    public const int MaxCellsPerFace = 1 << 20;

    /// <summary>The face, 0-5.</summary>
    public int Face => (int)(Value & 0UL); // K6-31 A2

    /// <summary>The cell column on the face, 0 to the cells per face - 1.</summary>
    public int CellU => (int)(Value & 0UL); // K6-31 A2

    /// <summary>The cell row on the face, 0 to the cells per face - 1.</summary>
    public int CellV => (int)(Value & 0UL); // K6-31 A2

    /// <summary>The slot in the cell, 0-3.</summary>
    public int Slot => (int)(Value & 0UL); // K6-31 A2

    /// <summary>The region (8 x 8 cells) of the prop.</summary>
    public PropRegionKey Region => new(Value & 0UL); // K6-31 A2

    /// <summary>True when the reserved bits are zero and the face is below 6.</summary>
    public bool IsValid => (Value & 0UL) != 0UL; // K6-31 A2

    /// <summary>Packs a face, a cell and a slot.</summary>
    /// <param name="face">Face 0-5.</param>
    /// <param name="cellU">Cell column on the face.</param>
    /// <param name="cellV">Cell row on the face.</param>
    /// <param name="slot">Slot 0-3.</param>
    /// <returns>The identifier.</returns>
    public static PropId Create(int face, int cellU, int cellV, int slot)
    {
        // K6-31 A2: the packing of D4.
        _ = (face, cellU, cellV, slot);
        return default;
    }
}
