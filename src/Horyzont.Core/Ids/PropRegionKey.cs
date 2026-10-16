namespace Horyzont.Core.Ids;

/// <summary>A region of 8 x 8 grid cells on a face (K6-31 D4): <see cref="PropId.Value"/> shifted right by 24 bits.</summary>
/// <param name="Value">Face, region U and region V packed as in <see cref="PropId"/>.</param>
public readonly record struct PropRegionKey(ulong Value)
{
    /// <summary>The face, 0-5.</summary>
    public int Face => (int)((Value >> 34) & 7UL);

    /// <summary>The region column on the face.</summary>
    public int RegionU => (int)((Value >> 17) & 0x1FFFFUL);

    /// <summary>The region row on the face.</summary>
    public int RegionV => (int)(Value & 0x1FFFFUL);

    /// <summary>True when only the 37 bits of face, region U and region V are set and the face is below 6.</summary>
    public bool IsValid => (Value >> 37) == 0UL && Face < 6;
}
