namespace Horyzont.Testing.Generators;

/// <summary>
/// 4096 packed block states of one chunk. FsCheck would treat a bare <c>uint[]</c> as "any array", so properties take this wrapper instead.
/// </summary>
/// <param name="Blocks">The blocks in <c>LocalIndex</c> order.</param>
#pragma warning disable CA1819 // The wrapper owns the array on purpose.
public sealed record ChunkBlocksCase(uint[] Blocks);
#pragma warning restore CA1819
