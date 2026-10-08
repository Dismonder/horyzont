using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Terrain;

/// <summary>Key of a 16³ terrain data block; ordered by planet, LOD, X, Y, Z.</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Lod">Level of detail (0 for edits).</param>
/// <param name="X">Block X.</param>
/// <param name="Y">Block Y.</param>
/// <param name="Z">Block Z.</param>
public readonly record struct TerrainBlockKey(PlanetId Planet, byte Lod, int X, int Y, int Z) : IComparable<TerrainBlockKey>
{
    /// <inheritdoc />
    public int CompareTo(TerrainBlockKey other)
    {
        var c = Planet.Value.CompareTo(other.Planet.Value);
        if (c != 0)
        {
            return c;
        }

        c = Lod.CompareTo(other.Lod);
        if (c != 0)
        {
            return c;
        }

        c = X.CompareTo(other.X);
        if (c != 0)
        {
            return c;
        }

        c = Y.CompareTo(other.Y);
        return c != 0 ? c : Z.CompareTo(other.Z);
    }

    /// <summary>Less than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <(TerrainBlockKey left, TerrainBlockKey right) => left.CompareTo(right) < 0;

    /// <summary>Less than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <=(TerrainBlockKey left, TerrainBlockKey right) => left.CompareTo(right) <= 0;

    /// <summary>Greater than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >(TerrainBlockKey left, TerrainBlockKey right) => left.CompareTo(right) > 0;

    /// <summary>Greater than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >=(TerrainBlockKey left, TerrainBlockKey right) => left.CompareTo(right) >= 0;
}
