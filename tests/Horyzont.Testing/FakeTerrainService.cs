using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Testing;

/// <summary>
/// Terrain of every planet is a sphere of <see cref="SurfaceRadius"/> around the planet centre. Records edits and lets
/// tests control which areas are loaded. Raycasts do not allocate.
/// Every edited block has deterministic "voxel data": XxHash64 of its previous data and the edit, so two services that applied the
/// same edits to the same starting data agree byte for byte. Blocks absent from memory come from <see cref="BlockSource"/>
/// (persistence tests point it at the saved blocks) or start empty, like generator output.
/// The block-data hashing (XxHash64 over the previous data and the edit fields) is pinned by the frozen golden terrain journal
/// corpus: changing it breaks the corpus for reasons unrelated to Core.
/// </summary>
public sealed class FakeTerrainService(float surfaceRadius = 4000f, float voxelSizeMeters = 1f) : ITerrainService
{
    private readonly Dictionary<TerrainBlockKey, byte[]> _blocks = [];
    private readonly List<TerrainBlockKey> _keys = [];

    public float SurfaceRadius { get; set; } = surfaceRadius;

    /// <summary>Saved data of a block that is not in memory; null when the block was never saved.</summary>
    public Func<TerrainBlockKey, byte[]?>? BlockSource { get; set; }

    /// <summary>Blocks in memory (edited or loaded from <see cref="BlockSource"/>).</summary>
    public IReadOnlyDictionary<TerrainBlockKey, byte[]> Blocks => _blocks;

    /// <summary>When true every area counts as loaded; otherwise only <see cref="LoadedAreas"/>.</summary>
    public bool AllAreasLoaded { get; set; } = true;

    public List<LoadedArea> LoadedAreas { get; } = [];

    public List<LoadedArea> BlockingLoads { get; } = [];

    public List<TerrainEditOp> AppliedEdits { get; } = [];

    /// <summary>The versions passed with every applied edit, in call order.</summary>
    public List<IReadOnlyList<TerrainBlockVersion>> AppliedVersions { get; } = [];

    /// <summary>Called at the start of <see cref="ApplyEdit"/> (tests inspect the state at call time).</summary>
    public Action<TerrainEditOp, IReadOnlyList<TerrainBlockVersion>>? OnApplyEdit { get; set; }

    /// <summary>Called at the end of <see cref="ApplyEdit"/>, when every block of the versions has its new data.</summary>
    public Action<TerrainEditOp, IReadOnlyList<TerrainBlockVersion>>? AfterApplyEdit { get; set; }

    /// <summary>When set, <see cref="LoadAreaBlocking"/> throws it (a failing disk read).</summary>
    public Exception? ThrowOnLoad { get; set; }

    /// <summary>When set, <see cref="ApplyEdit"/> throws it.</summary>
    public Exception? ThrowOnApply { get; set; }

    /// <summary>Result of <see cref="MeasureSphere"/>; null makes it return false (nothing measurable).</summary>
    public MeasuredSphere? MeasureResult { get; set; }

    /// <summary>Number of <see cref="MeasureSphere"/> calls.</summary>
    public int MeasureCalls { get; private set; }

    /// <summary>
    /// Radius of the generated surface (a flat sphere around the planet centre) that <see cref="TrySampleGeneratedSurface"/> reports; null
    /// makes it return false (no surface), so the props of a planet do not exist (K6-31 D6).
    /// </summary>
    public float? GeneratedSurfaceRadius { get; set; }

    /// <summary>Number of <see cref="TrySampleGeneratedSurface"/> calls.</summary>
    public int GeneratedSurfaceCalls { get; private set; }

    /// <summary>The generated surface radius in a direction (K6-31 D6): <see cref="GeneratedSurfaceRadius"/> when it is set.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="direction">Unit direction from the planet centre.</param>
    /// <param name="radius">The radius when the surface is known.</param>
    /// <returns>True when <see cref="GeneratedSurfaceRadius"/> is set.</returns>
    public bool TrySampleGeneratedSurface(PlanetId planet, Vector3 direction, out float radius)
    {
        GeneratedSurfaceCalls++;
        radius = GeneratedSurfaceRadius ?? 0f;
        return GeneratedSurfaceRadius is not null;
    }

    public bool MeasureSphere(PlanetId planet, Vector3 center, float radius, Span<int> solidVoxelsByMaterial, out int airVoxels)
    {
        MeasureCalls++;
        solidVoxelsByMaterial.Clear();
        airVoxels = 0;
        if (MeasureResult is not { } result)
        {
            return false;
        }

        foreach (var (material, count) in result.Solid)
        {
            solidVoxelsByMaterial[material] = count;
        }

        airVoxels = result.Air;
        return true;
    }

    public bool IsAreaLoaded(PlanetId planet, Vector3 center, float radius)
    {
        if (AllAreasLoaded)
        {
            return true;
        }

        foreach (var area in LoadedAreas)
        {
            if (area.Planet == planet && Vector3.Distance(area.Center, center) + radius <= area.Radius)
            {
                return true;
            }
        }

        return false;
    }

    public void LoadAreaBlocking(PlanetId planet, Vector3 center, float radius)
    {
        if (ThrowOnLoad is { } exception)
        {
            throw exception;
        }

        var area = new LoadedArea(planet, center, radius);
        BlockingLoads.Add(area);
        LoadedAreas.Add(area);
        TerrainMath.AffectedBlocks(planet, center, radius, voxelSizeMeters, 0, _keys);
        foreach (var key in _keys)
        {
            if (!_blocks.ContainsKey(key) && BlockSource?.Invoke(key) is { } saved)
            {
                _blocks[key] = saved;
            }
        }
    }

    /// <summary>
    /// Answers <see cref="TrySampleSolid"/> for tests with their own terrain shape (flat ground, a hill); null = the sphere of
    /// <see cref="SurfaceRadius"/>. Only called for points in loaded areas.
    /// </summary>
    public Func<PlanetId, Vector3, bool>? SolidModel { get; set; }

    /// <summary>
    /// Caves (K6-34): spheres of air inside the terrain. Empty by default, which leaves <see cref="Raycast"/> exactly as it was: a ray that starts below the surface hits at once.
    /// </summary>
    public List<TerrainCavity> Cavities { get; } = [];

    /// <summary>Counts the calls of <see cref="TrySampleSolid"/>.</summary>
    public int SolidSamples { get; private set; }

    /// <inheritdoc />
    public bool TrySampleSolid(PlanetId planet, Vector3 position, out bool solid)
    {
        SolidSamples++;
        solid = false;
        if (!IsAreaLoaded(planet, position, 0f))
        {
            return false;
        }

        solid = SolidModel?.Invoke(planet, position) ?? position.LengthSquared() < SurfaceRadius * SurfaceRadius;
        return true;
    }

    /// <summary>Current data of a block: memory, then <see cref="BlockSource"/>, else empty (generator output).</summary>
    public byte[] GetBlockData(TerrainBlockKey key) =>
        _blocks.TryGetValue(key, out var data) ? data : BlockSource?.Invoke(key) ?? [];

    public bool Raycast(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
    {
        hit = default;
        var r = SurfaceRadius;
        var c = origin.LengthSquared() - (r * r);
        if (c <= 0)
        {
            // Inside a cave the ray hits the wall where it leaves the cave, unless it leaves above the surface of the planet.
            for (var i = 0; i < Cavities.Count; i++)
            {
                var cave = Cavities[i];
                var offset = origin - cave.Center;
                var caveC = offset.LengthSquared() - (cave.Radius * cave.Radius);
                if (caveC >= 0)
                {
                    continue;
                }

                var caveB = Vector3.Dot(offset, direction);
                var exit = -caveB + MathF.Sqrt((caveB * caveB) - caveC);
                if (exit > maxDistance)
                {
                    return false;
                }

                var wall = origin + (direction * exit);
                if (wall.LengthSquared() > r * r)
                {
                    return false;
                }

                hit = new TerrainHit(wall, Vector3.Normalize(cave.Center - wall), exit);
                return true;
            }

            // Inside the terrain: hit right at the origin.
            hit = new TerrainHit(origin, TerrainMath.UpAt(origin), 0f);
            return true;
        }

        var b = Vector3.Dot(origin, direction);
        var discriminant = (b * b) - c;
        if (discriminant < 0)
        {
            return false;
        }

        var t = -b - MathF.Sqrt(discriminant);
        if (t < 0 || t > maxDistance)
        {
            return false;
        }

        var position = origin + (direction * t);
        hit = new TerrainHit(position, TerrainMath.UpAt(position), t);
        return true;
    }

    /// <summary>
    /// Applies the edit; throws <see cref="InvalidOperationException"/> before any change when the keys of
    /// <paramref name="versions"/> differ from <see cref="TerrainMath.AffectedBlocks"/> with the margin constant.
    /// </summary>
    public void ApplyEdit(TerrainEditOp edit, IReadOnlyList<TerrainBlockVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(versions);
        OnApplyEdit?.Invoke(edit, versions);
        if (ThrowOnApply is { } exception)
        {
            throw exception;
        }

        TerrainMath.AffectedBlocks(edit.Planet, edit.Center, edit.Radius, voxelSizeMeters, TerrainRules.EditMarginVoxels, _keys);
        if (versions.Count != _keys.Count)
        {
            throw new InvalidOperationException($"ApplyEdit got {versions.Count} versions for {_keys.Count} affected blocks.");
        }

        for (var i = 0; i < _keys.Count; i++)
        {
            if (versions[i].Key != _keys[i])
            {
                throw new InvalidOperationException($"ApplyEdit version {i} is for {versions[i].Key}, expected {_keys[i]}.");
            }
        }

        AppliedEdits.Add(edit);
        AppliedVersions.Add(versions);
        Span<byte> fields = stackalloc byte[27];
        BinaryPrimitives.WriteUInt64LittleEndian(fields, edit.Planet.Value);
        fields[8] = (byte)edit.Shape;
        BinaryPrimitives.WriteSingleLittleEndian(fields[9..], edit.Center.X);
        BinaryPrimitives.WriteSingleLittleEndian(fields[13..], edit.Center.Y);
        BinaryPrimitives.WriteSingleLittleEndian(fields[17..], edit.Center.Z);
        BinaryPrimitives.WriteSingleLittleEndian(fields[21..], edit.Radius);
        fields[25] = (byte)edit.Mode;
        fields[26] = edit.MaterialId;
        foreach (var key in _keys)
        {
            var hash = new XxHash64();
            hash.Append(GetBlockData(key));
            hash.Append(fields);
            _blocks[key] = hash.GetCurrentHash();
        }

        AfterApplyEdit?.Invoke(edit, versions);
    }
}
