using Horyzont.Core.Checkpoints;

namespace Horyzont.Constructs.Scheduling;

/// <summary>Total order of chunk keys: by construct entity id, then by chunk position.</summary>
public sealed class ConstructChunkKeyComparer : IComparer<ConstructChunkKey>
{
    /// <summary>The shared stateless instance.</summary>
    public static readonly ConstructChunkKeyComparer Instance = new();

    private ConstructChunkKeyComparer()
    {
    }

    /// <inheritdoc />
    public int Compare(ConstructChunkKey x, ConstructChunkKey y)
    {
        var construct = x.Construct.Value.CompareTo(y.Construct.Value);
        return construct != 0 ? construct : x.Chunk.CompareTo(y.Chunk);
    }
}
