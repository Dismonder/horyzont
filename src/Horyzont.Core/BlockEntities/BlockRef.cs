using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.BlockEntities;

/// <summary>
/// Key of a block with state (K6-30): a construct and one of its cells. Ordered by construct id, then by chunk and index in the chunk
/// of the cell (the order of the chunk checkpoints).
/// </summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell of the block.</param>
public readonly record struct BlockRef(EntityId Construct, CellPos Cell) : IComparable<BlockRef>
{
    /// <inheritdoc />
    public int CompareTo(BlockRef other)
    {
        var construct = Construct.Value.CompareTo(other.Construct.Value);
        if (construct != 0)
        {
            return construct;
        }

        var chunk = Cell.Chunk.CompareTo(other.Cell.Chunk);
        return chunk != 0 ? chunk : Cell.LocalIndex.CompareTo(other.Cell.LocalIndex);
    }

    /// <summary>Less than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <(BlockRef left, BlockRef right) => left.CompareTo(right) < 0;

    /// <summary>Less than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <=(BlockRef left, BlockRef right) => left.CompareTo(right) <= 0;

    /// <summary>Greater than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >(BlockRef left, BlockRef right) => left.CompareTo(right) > 0;

    /// <summary>Greater than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >=(BlockRef left, BlockRef right) => left.CompareTo(right) >= 0;
}
