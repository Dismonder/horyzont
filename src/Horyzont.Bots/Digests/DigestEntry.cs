using System.Numerics;

namespace Horyzont.Bots.Digests;

/// <summary>What a digest entry describes.</summary>
public enum DigestKind
{
    /// <summary>A construct: id, frame, pose, version and blocks.</summary>
    Construct = 0,

    /// <summary>A LOD zero terrain block: key, version and the hash of its envelope.</summary>
    Terrain = 1,
}

/// <summary>
/// One entry of a state dump: the server and every bot write the same entries for what they hold, and <c>tools/coop/compare_dumps.py</c>
/// compares them by <see cref="Kind"/> and <see cref="Key"/>.
/// </summary>
/// <param name="Kind">Kind of the entry.</param>
/// <param name="Key">Key in the text form of its kind (construct id; terrain <c>planet/lod/x/y/z</c>).</param>
/// <param name="Version">Version of the construct or of the terrain block.</param>
/// <param name="Hash">Content hash (<see cref="ConstructDigest.HashOf"/>; terrain envelope hash, 0 when unknown).</param>
/// <param name="SortKey">Numeric order inside the kind, so dumps list keys in their natural order.</param>
/// <param name="Position">
/// Where the entry is in its parent frame (the origin of a construct), for the coverage of the shared zone; not part of any digest.
/// </param>
/// <param name="Voxels">
/// Voxel digest of a terrain block (<c>TerrainBlockCodec.Digest</c>) where the writer holds voxels (game server, bots in the engine);
/// null for bots without an engine. Not part of <see cref="DigestDump.Combine"/>.
/// </param>
public readonly record struct DigestEntry(
    DigestKind Kind, string Key, uint Version, ulong Hash, (long A, long B, long C, long D, long E) SortKey, Vector3? Position = null,
    ulong? Voxels = null);

/// <summary>Orders entries by kind, then by the natural order of their keys.</summary>
public sealed class DigestEntryComparer : IComparer<DigestEntry>
{
    /// <summary>Gets the shared instance.</summary>
    public static DigestEntryComparer Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(DigestEntry x, DigestEntry y)
    {
        var kind = x.Kind.CompareTo(y.Kind);
        if (kind != 0)
        {
            return kind;
        }

        var key = x.SortKey.CompareTo(y.SortKey);
        return key != 0 ? key : string.CompareOrdinal(x.Key, y.Key);
    }
}
