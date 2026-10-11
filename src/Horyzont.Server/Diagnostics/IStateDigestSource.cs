namespace Horyzont.Server.Diagnostics;

/// <summary>
/// Digests of the authoritative state for the identity checks of K4-19 (the <c>digest</c> console command): constructs, terrain block
/// versions and, where the process holds voxels, the voxel digest. The game implements it; the server only calls it on the main thread
/// and only on demand (computing sorts and allocates).
/// </summary>
public interface IStateDigestSource
{
    /// <summary>Writes the entries of the state as sorted JSON lines (the format of <c>Horyzont.Bots.Digests.DigestDump</c>).</summary>
    /// <param name="writer">Destination.</param>
    void WriteDump(TextWriter writer);

    /// <summary>Computes the digests.</summary>
    /// <returns>The digests.</returns>
    StateDigest Compute();
}

/// <summary>Digests of the authoritative state.</summary>
/// <param name="Constructs">Digest of all constructs.</param>
/// <param name="TerrainVersions">Digest of all edited terrain block versions.</param>
/// <param name="TerrainVoxels">Digest of the voxels of the edited blocks; null when the process has no voxels.</param>
public readonly record struct StateDigest(ulong Constructs, ulong TerrainVersions, ulong? TerrainVoxels);
