namespace Horyzont.Core.Checkpoints;

/// <summary>Cells of one construct chunk; the array (4096 packed values) is owned by the record.</summary>
/// <param name="Key">Chunk key.</param>
/// <param name="Blocks">Packed <see cref="World.Constructs.BlockState"/> values indexed by local index.</param>
#pragma warning disable CA1819 // The record owns the array; codecs and the loader read it directly.
public sealed record ConstructChunkRecord(ConstructChunkKey Key, uint[] Blocks);
#pragma warning restore CA1819
