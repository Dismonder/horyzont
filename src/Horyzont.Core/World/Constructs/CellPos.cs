namespace Horyzont.Core.World.Constructs;

/// <summary>Cell of a construct; valid range −2048..2047 on each axis.</summary>
/// <param name="X">X coordinate.</param>
/// <param name="Y">Y coordinate.</param>
/// <param name="Z">Z coordinate.</param>
public readonly record struct CellPos(int X, int Y, int Z)
{
    /// <summary>Half extent of the valid range: −Limit .. Limit − 1 on each axis.</summary>
    public const int Limit = 2048;

    /// <summary>True when every coordinate is in −2048..2047.</summary>
    public bool IsInBounds =>
        X >= -Limit && X < Limit && Y >= -Limit && Y < Limit && Z >= -Limit && Z < Limit;

    /// <summary>Chunk containing the cell (arithmetic shift is floor division also for negative values).</summary>
    public ChunkPos Chunk => new(X >> 4, Y >> 4, Z >> 4);

    /// <summary>Index of the cell inside its chunk, 0..4095.</summary>
    public int LocalIndex => ((X & 15) << 8) | ((Y & 15) << 4) | (Z & 15);

    /// <summary>Neighbouring cell across a face.</summary>
    /// <param name="face">The face.</param>
    /// <returns>The neighbour.</returns>
    public CellPos Offset(Face face) => face switch
    {
        Face.PosX => this with { X = X + 1 },
        Face.NegX => this with { X = X - 1 },
        Face.PosY => this with { Y = Y + 1 },
        Face.NegY => this with { Y = Y - 1 },
        Face.PosZ => this with { Z = Z + 1 },
        Face.NegZ => this with { Z = Z - 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown face."),
    };

    /// <summary>Cell from a chunk and a local index.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="localIndex">Index 0..4095.</param>
    /// <returns>The cell.</returns>
    public static CellPos FromChunk(ChunkPos chunk, int localIndex) =>
        new((chunk.X << 4) | ((localIndex >> 8) & 15), (chunk.Y << 4) | ((localIndex >> 4) & 15), (chunk.Z << 4) | (localIndex & 15));
}
