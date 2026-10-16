namespace Horyzont.Core.Ids;

/// <summary>
/// Identifier of a generated prop (K6-31 D4): the position in the cube-sphere grid of the planet, independent of the terrain. Bits 63-61 are
/// reserved (0), 60-58 face 0-5, 57-41 region U, 40-24 region V, 23-21 cell U in the region, 20-18 cell V in the region, 17-16 slot 0-3, 15-0
/// reserved (0). The layout is frozen by the A1 review. <c>PropId(0)</c> is a valid identifier (face 0, cell 0,0, slot 0): never use
/// <c>default</c> as "none".
/// </summary>
/// <param name="Value">The packed value.</param>
public readonly record struct PropId(ulong Value)
{
    /// <summary>Most cells per face edge an identifier can hold (17 bits of region times 8 cells).</summary>
    public const int MaxCellsPerFace = 1 << 20;

    private const ulong ReservedMask = (7UL << 61) | 0xFFFFUL;

    /// <summary>The face, 0-5 (a larger value means the identifier is not valid).</summary>
    public int Face => (int)((Value >> 58) & 7UL);

    /// <summary>The cell column on the face, 0 to the cells per face - 1.</summary>
    public int CellU => (int)((((Value >> 41) & 0x1FFFFUL) << 3) | ((Value >> 21) & 7UL));

    /// <summary>The cell row on the face, 0 to the cells per face - 1.</summary>
    public int CellV => (int)((((Value >> 24) & 0x1FFFFUL) << 3) | ((Value >> 18) & 7UL));

    /// <summary>The slot in the cell, 0-3.</summary>
    public int Slot => (int)((Value >> 16) & 3UL);

    /// <summary>The region (8 x 8 cells) of the prop.</summary>
    public PropRegionKey Region => new(Value >> 24);

    /// <summary>True when the reserved bits are zero and the face is below 6.</summary>
    public bool IsValid => (Value & ReservedMask) == 0UL && Face < 6;

    /// <summary>Packs a face, a cell and a slot.</summary>
    /// <param name="face">Face 0-5.</param>
    /// <param name="cellU">Cell column on the face.</param>
    /// <param name="cellV">Cell row on the face.</param>
    /// <param name="slot">Slot 0-3.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside its range.</exception>
    public static PropId Create(int face, int cellU, int cellV, int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(face, 5);
        ArgumentOutOfRangeException.ThrowIfNegative(cellU);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cellU, MaxCellsPerFace);
        ArgumentOutOfRangeException.ThrowIfNegative(cellV);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cellV, MaxCellsPerFace);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 3);

        return new PropId(
            ((ulong)face << 58) | ((ulong)(cellU >> 3) << 41) | ((ulong)(cellV >> 3) << 24) | ((ulong)(cellU & 7) << 21) | ((ulong)(cellV & 7) << 18)
            | ((ulong)slot << 16));
    }
}
