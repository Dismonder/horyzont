using Godot;

using Horyzont.Persistence.Terrain;

namespace Horyzont.Game.Terrain;

/// <summary>
/// Every parameter of the Voxel Tools edit that decides the voxel bytes after an edit (K2-09, gate decision D1). Nothing here is
/// configurable at run time. Changing any constant, <c>voxel.ref</c> in <c>engine/engine.json</c> or the generator profile used by
/// the edited planets changes the replay of old journal tails: bump <see cref="SemanticsVersion"/> and re-record
/// <c>EditSemantics_GoldenDigest</c> (ZASADY point 3).
/// </summary>
public static class TerrainEditSemantics
{
    /// <summary>Version of the edit semantics; saved in the world meta table as part of <see cref="Current"/>.</summary>
    public const ushort SemanticsVersion = 1;

    /// <summary>Voxel Tools release the semantics were recorded with; equals <c>voxel.ref</c> in <c>engine/engine.json</c> (engine test).</summary>
    public const string VoxelRef = "v1.7";

    /// <summary>
    /// Format of the voxel payload inside the terrain block blob: 1 = <c>VoxelBlockSerializer</c> of Voxel Tools v1.7,
    /// channels of <see cref="Planet.TerrainChannels"/> (16-bit SDF, 8-bit texture index). A new serializer or channel layout gets
    /// a new number and the decoder keeps reading the old one.
    /// </summary>
    public const byte VoxelFormat = 1;

    /// <summary>SDF scale of the sphere tool (<c>VoxelTool.sdf_scale</c>).</summary>
    public const float SdfScale = 1f;

    /// <summary>SDF strength of the sphere tool (<c>VoxelTool.sdf_strength</c>): 1 = full union or subtraction.</summary>
    public const float SdfStrength = 1f;

    /// <summary>
    /// Material write of an add edit, added to the edit radius. The SDF add of Voxel Tools writes only the SDF channel, so a second
    /// sphere with the same centre and this radius sets the texture index channel to the material for every voxel strictly inside
    /// it (the voxels whose SDF the union made negative). A remove edit leaves the material channel as it is.
    /// </summary>
    public const float MaterialSphereRadiusOffset = 0f;

    // Raycast parameters: the result feeds operations but is not replayed, so changing them needs no SemanticsVersion bump.

    /// <summary>
    /// Share of the sampled SDF distance the ray march of <c>Raycast</c> advances per step (sphere tracing). Below 1 because the
    /// trilinear field can be steeper than a true distance (up to √3 for 1-Lipschitz voxels, more next to edited voxels).
    /// </summary>
    public const double RaycastSdfStepFactor = 0.5;

    /// <summary>
    /// Shortest step of the ray march in metres (0.1 voxel): near the surface the march never advances less, so a solid interval of
    /// at least this length on the ray is never stepped over.
    /// </summary>
    public const double RaycastMinStepMeters = 0.1;

    /// <summary>Most steps of one ray march; a ray still in the air after them misses.</summary>
    public const int RaycastMaxSteps = 1024;

    /// <summary>Binary search iterations of <c>Raycast</c> after a sign change (near the surface the last step is the minimum step: 0.1 m / 2^10, about 0.1 mm).</summary>
    public const int RaycastBinarySearchIterations = 10;

    /// <summary>Compression of the voxel payload in the blob (zstd; decoding does not depend on it).</summary>
    public const VoxelBlockSerializer.Compression BlobCompression = VoxelBlockSerializer.Compression.Zstd;

    /// <summary>The semantics string saved under <c>meta.terrain_semantics</c>: <c>"{SemanticsVersion}|voxel={VoxelRef}"</c>.</summary>
    public static string Current { get; } = TerrainSemanticsMeta.Format(SemanticsVersion, VoxelRef);
}
