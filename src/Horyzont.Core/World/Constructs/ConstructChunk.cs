namespace Horyzont.Core.World.Constructs;

/// <summary>16³ cells of a construct.</summary>
public sealed class ConstructChunk
{
    /// <summary>Cells per axis.</summary>
    public const int Size = 16;

    /// <summary>Cells per chunk.</summary>
    public const int CellCount = Size * Size * Size;

    private readonly uint[] _blocks = new uint[CellCount];

    internal ConstructChunk()
    {
    }

    /// <summary>Number of non-empty cells.</summary>
    public int NonEmptyCount { get; private set; }

    /// <summary>Raw packed values indexed by <see cref="CellPos.LocalIndex"/>.</summary>
    public ReadOnlySpan<uint> Blocks => _blocks;

    /// <summary>State of a cell.</summary>
    /// <param name="index">Local index 0..4095.</param>
    /// <returns>The state.</returns>
    public BlockState Get(int index) => new(_blocks[index]);

    /// <summary>Sets a cell and returns its previous state.</summary>
    internal BlockState Set(int index, BlockState state)
    {
        var previous = new BlockState(_blocks[index]);
        _blocks[index] = state.Packed;
        if (previous.IsEmpty && !state.IsEmpty)
        {
            NonEmptyCount++;
        }
        else if (!previous.IsEmpty && state.IsEmpty)
        {
            NonEmptyCount--;
        }

        return previous;
    }

    /// <summary>Copy of the raw values owned by the caller.</summary>
    internal uint[] ToArray() => (uint[])_blocks.Clone();
}
