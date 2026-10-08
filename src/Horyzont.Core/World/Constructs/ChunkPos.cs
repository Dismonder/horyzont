namespace Horyzont.Core.World.Constructs;

/// <summary>Position of a 16³ chunk of a construct; ordered lexicographically by X, Y, Z.</summary>
/// <param name="X">X coordinate.</param>
/// <param name="Y">Y coordinate.</param>
/// <param name="Z">Z coordinate.</param>
public readonly record struct ChunkPos(int X, int Y, int Z) : IComparable<ChunkPos>
{
    /// <inheritdoc />
    public int CompareTo(ChunkPos other)
    {
        var x = X.CompareTo(other.X);
        if (x != 0)
        {
            return x;
        }

        var y = Y.CompareTo(other.Y);
        return y != 0 ? y : Z.CompareTo(other.Z);
    }

    /// <summary>Less than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <(ChunkPos left, ChunkPos right) => left.CompareTo(right) < 0;

    /// <summary>Less than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <=(ChunkPos left, ChunkPos right) => left.CompareTo(right) <= 0;

    /// <summary>Greater than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >(ChunkPos left, ChunkPos right) => left.CompareTo(right) > 0;

    /// <summary>Greater than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >=(ChunkPos left, ChunkPos right) => left.CompareTo(right) >= 0;
}
