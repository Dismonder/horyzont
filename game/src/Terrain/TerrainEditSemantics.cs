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

    /// <summary>Step of the ray march of <c>Raycast</c> in metres.</summary>
    public const float RaycastStepMeters = 0.5f;

    /// <summary>Binary search iterations of <c>Raycast</c> after a sign change (0.5 m / 2^10, about 0.5 mm).</summary>
    public const int RaycastBinarySearchIterations = 10;

    /// <summary>Compression of the voxel payload in the blob (zstd; decoding does not depend on it).</summary>
    public const VoxelBlockSerializer.Compression BlobCompression = VoxelBlockSerializer.Compression.Zstd;

    /// <summary>The semantics string saved under <c>meta.terrain_semantics</c>: <c>"{SemanticsVersion}|voxel={VoxelRef}"</c>.</summary>
    public static string Current { get; } = TerrainSemanticsMeta.Format(SemanticsVersion, VoxelRef);
}
