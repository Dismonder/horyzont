using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Generators;

/// <summary>
/// Graph of the generator profile <c>core:planet/1</c>: a sphere with continents, ridges, detail relief, caves, five materials
/// and an always-solid core. Everything the output depends on is a constant here or comes from <see cref="PlanetParams"/>
/// (seed, radius, core depth). FROZEN once merged to main: any change of a constant, a noise seed or a graph setting is a new
/// profile next to this one (ZASADY point 3, test <c>GeneratorV1_GoldenDigest</c>).
/// </summary>
public static class PlanetGeneratorV1
{
    /// <summary>Hard limit of the surface height above and below the nominal radius.</summary>
    public const float MaxReliefMeters = 300f;

    public const float ContinentAmplitude = 120f;
    public const float ContinentFrequency = 1f / 1200f;
    public const int ContinentOctaves = 4;
    public const float DetailAmplitude = 25f;
    public const float DetailFrequency = 1f / 150f;
    public const int DetailOctaves = 3;
    public const float RidgeAmplitude = 150f;
    public const float RidgeFrequency = 1f / 600f;
    public const int RidgeOctaves = 4;
    public const float CaveFrequency = 1f / 48f;
    public const float CaveWidth = 0.08f;
    public const float CaveScale = 24f;
    public const float CaveMinDepth = 6f;
    public const float CaveCoreMargin = 20f;
    public const float CaveFadeMeters = 4f;
    public const float OreFrequency = 1f / 16f;
    public const float OreThreshold = 0.72f;
    public const float DirtDepth = 3f;
    public const float SandDepth = 4f;
    public const float SandBelowHeight = -60f;
    public const float CoreSurfaceTolerance = 0.5f;

    // Graph compilation settings affect the bytes of the blocks, so they are fixed explicitly (S5).
    public const float SdfClipThreshold = 1.5f;
    public const bool UseSubdivision = true;
    public const int SubdivisionSize = 16;
    public const bool UseOptimizedExecutionMap = true;
    public const bool UseXzCaching = true;

    /// <summary>Builds and compiles the generator for a planet. Main thread.</summary>
    public static VoxelGeneratorGraph Create(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var radius = planet.RadiusMeters;
        var coreRadius = radius - planet.CoreDepthMeters;

        var generator = new VoxelGeneratorGraph();
        var g = new GraphBuilder(generator);
        var x = g.X;
        var y = g.Y;
        var z = g.Z;
        var r = g.Length(x, y, z);

        // Noise is sampled on the nominal sphere (direction scaled to R) so relief does not depend on the altitude.
        var scale = g.Div(g.Const(radius), g.Max(r, g.Const(1f)));
        var px = g.Mul(x, scale);
        var py = g.Mul(y, scale);
        var pz = g.Mul(z, scale);

        var continent = g.Noise3D(px, py, pz, new NoiseSpec(
            NoiseSeeds.For(planet.Seed, NoiseSeeds.ContinentSlot), ContinentFrequency, Zn_FastNoiseLite.FractalTypeEnum.Fbm, ContinentOctaves));
        var detail = g.Noise3D(px, py, pz, new NoiseSpec(
            NoiseSeeds.For(planet.Seed, NoiseSeeds.DetailSlot), DetailFrequency, Zn_FastNoiseLite.FractalTypeEnum.Fbm, DetailOctaves));
        var ridges = g.Noise3D(px, py, pz, new NoiseSpec(
            NoiseSeeds.For(planet.Seed, NoiseSeeds.RidgesSlot), RidgeFrequency, Zn_FastNoiseLite.FractalTypeEnum.Ridged, RidgeOctaves));

        var land = g.Clamp(continent, 0f, 1f);
        var heightRaw = g.Add(
            g.Add(g.Mul(continent, g.Const(ContinentAmplitude)), g.Mul(detail, g.Const(DetailAmplitude))),
            g.Mul(g.Mul(ridges, g.Const(RidgeAmplitude)), land));
        var height = g.Clamp(heightRaw, -MaxReliefMeters, MaxReliefMeters);

        var surfaceRadius = g.Add(g.Const(radius), height);
        var ground = g.Sub(r, surfaceRadius);
        var depth = g.Sub(surfaceRadius, r);

        // Caves: |noise| < width, only deep enough below the surface and away from the core.
        var caveNoise = g.Noise3D(x, y, z, new NoiseSpec(
            NoiseSeeds.For(planet.Seed, NoiseSeeds.CavesSlot), CaveFrequency, Zn_FastNoiseLite.FractalTypeEnum.None, 1));
        var caveMask = g.Mul(
            g.Smoothstep(depth, CaveMinDepth, CaveMinDepth + CaveFadeMeters),
            g.Smoothstep(r, coreRadius + CaveCoreMargin, coreRadius + CaveCoreMargin + CaveFadeMeters));
        var cave = g.Sub(
            g.Mul(g.Sub(g.Const(CaveWidth), g.Abs(caveNoise)), g.Const(CaveScale)),
            g.Mul(g.Sub(g.Const(1f), caveMask), g.Const(1000f)));

        var sdf = g.Min(g.Max(ground, cave), g.Sub(r, g.Const(coreRadius)));
        g.OutputSdf(sdf);

        var ore = g.Noise3D(x, y, z, new NoiseSpec(
            NoiseSeeds.For(planet.Seed, NoiseSeeds.OreSlot), OreFrequency, Zn_FastNoiseLite.FractalTypeEnum.None, 1));
        var material = g.Select(g.Const(TerrainMaterials.StoneTextureIndex), g.Const(TerrainMaterials.OreTextureIndex), ore, OreThreshold);
        material = g.Select(g.Const(TerrainMaterials.DirtTextureIndex), material, depth, DirtDepth);
        var sandTest = g.Max(g.Sub(depth, g.Const(SandDepth)), g.Sub(height, g.Const(SandBelowHeight)));
        material = g.Select(g.Const(TerrainMaterials.SandTextureIndex), material, sandTest, 0f);
        material = g.Select(g.Const(TerrainMaterials.CoreTextureIndex), material, r, coreRadius + CoreSurfaceTolerance);
        g.OutputTexture(material);

        g.Compile();
        generator.SetTextureMode(VoxelGeneratorGraph.TextureModeEnum.Single);
        generator.SetSdfClipThreshold(SdfClipThreshold);
        generator.SetUseSubdivision(UseSubdivision);
        generator.SetSubdivisionSize(SubdivisionSize);
        generator.SetUseOptimizedExecutionMap(UseOptimizedExecutionMap);
        generator.SetUseXzCaching(UseXzCaching);
        return generator;
    }
}
