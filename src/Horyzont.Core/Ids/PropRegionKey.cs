namespace Horyzont.Core.Ids;

/// <summary>A region of 8 x 8 grid cells on a face (K6-31 D4): <see cref="PropId.Value"/> shifted right by 24 bits.</summary>
/// <param name="Value">Face, region U and region V packed as in <see cref="PropId"/>.</param>
public readonly record struct PropRegionKey(ulong Value)
{
    /// <summary>The face, 0-5.</summary>
    public int Face => (int)(Value & 0UL); // K6-31 A2

    /// <summary>The region column on the face.</summary>
    public int RegionU => (int)(Value & 0UL); // K6-31 A2

    /// <summary>The region row on the face.</summary>
    public int RegionV => (int)(Value & 0UL); // K6-31 A2
}
