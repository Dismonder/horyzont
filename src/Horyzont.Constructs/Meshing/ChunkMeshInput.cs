using Horyzont.Constructs.Sources;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Meshing;

/// <summary>A chunk copied with a one-cell border from its six neighbours; reusable, one per job slot.</summary>
public sealed class ChunkMeshInput
{
    /// <summary>Cells per axis including the border.</summary>
    public const int Padded = ConstructChunk.Size + 2;

    /// <summary>Stride of the padded X axis.</summary>
    public const int StrideX = Padded * Padded;

    /// <summary>Stride of the padded Y axis.</summary>
    public const int StrideY = Padded;

    private const int Size = ConstructChunk.Size;

    private readonly uint[] _cells = new uint[Padded * Padded * Padded];

    /// <summary>Chunk this input describes.</summary>
    public ConstructChunkKey Key { get; private set; }

    /// <summary>Generation of the request; results older than the latest generation are dropped.</summary>
    public uint Generation { get; private set; }

    /// <summary>Raw blocks; index <c>(x+1)*324 + (y+1)*18 + (z+1)</c> for x, y, z in −1..16.</summary>
    public Span<uint> Cells => _cells;

    /// <summary>Index of a padded cell; coordinates in −1..16.</summary>
    /// <param name="x">X in −1..16.</param>
    /// <param name="y">Y in −1..16.</param>
    /// <param name="z">Z in −1..16.</param>
    /// <returns>The index into <see cref="Cells"/>.</returns>
    public static int Index(int x, int y, int z) => ((x + 1) * StrideX) + ((y + 1) * StrideY) + (z + 1);

    /// <summary>Empties the input and assigns it a chunk and generation.</summary>
    /// <param name="key">Chunk.</param>
    /// <param name="generation">Request generation.</param>
    public void Reset(ConstructChunkKey key, uint generation)
    {
        Key = key;
        Generation = generation;
        _cells.AsSpan().Clear();
    }

    /// <summary>Main thread: copies the chunk and the 6 face slabs of the neighbours (edges and corners stay empty).</summary>
    /// <param name="source">Construct source.</param>
    /// <param name="key">Chunk to copy.</param>
    /// <param name="generation">Request generation.</param>
    /// <returns>False when the chunk does not exist (nothing to mesh).</returns>
    public bool Fill(IConstructSource source, ConstructChunkKey key, uint generation)
    {
        ArgumentNullException.ThrowIfNull(source);
        Reset(key, generation);
        if (!source.TryGetChunkBlocks(key.Construct, key.Chunk, out var blocks))
        {
            return false;
        }

        var cells = _cells.AsSpan();
        for (var x = 0; x < Size; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                blocks.Slice((x << 8) | (y << 4), Size).CopyTo(cells.Slice(Index(x, y, 0), Size));
            }
        }

        var c = key.Chunk;
        if (source.TryGetChunkBlocks(key.Construct, c with { X = c.X + 1 }, out var px))
        {
            for (var y = 0; y < Size; y++)
            {
                px.Slice(y << 4, Size).CopyTo(cells.Slice(Index(Size, y, 0), Size));
            }
        }

        if (source.TryGetChunkBlocks(key.Construct, c with { X = c.X - 1 }, out var nx))
        {
            for (var y = 0; y < Size; y++)
            {
                nx.Slice(((Size - 1) << 8) | (y << 4), Size).CopyTo(cells.Slice(Index(-1, y, 0), Size));
            }
        }

        if (source.TryGetChunkBlocks(key.Construct, c with { Y = c.Y + 1 }, out var py))
        {
            for (var x = 0; x < Size; x++)
            {
                py.Slice(x << 8, Size).CopyTo(cells.Slice(Index(x, Size, 0), Size));
            }
        }

        if (source.TryGetChunkBlocks(key.Construct, c with { Y = c.Y - 1 }, out var ny))
        {
            for (var x = 0; x < Size; x++)
            {
                ny.Slice((x << 8) | ((Size - 1) << 4), Size).CopyTo(cells.Slice(Index(x, -1, 0), Size));
            }
        }

        if (source.TryGetChunkBlocks(key.Construct, c with { Z = c.Z + 1 }, out var pz))
        {
            for (var x = 0; x < Size; x++)
            {
                for (var y = 0; y < Size; y++)
                {
                    cells[Index(x, y, Size)] = pz[(x << 8) | (y << 4)];
                }
            }
        }

        if (source.TryGetChunkBlocks(key.Construct, c with { Z = c.Z - 1 }, out var nz))
        {
            for (var x = 0; x < Size; x++)
            {
                for (var y = 0; y < Size; y++)
                {
                    cells[Index(x, y, -1)] = nz[(x << 8) | (y << 4) | (Size - 1)];
                }
            }
        }

        return true;
    }
}
