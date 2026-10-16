using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Rendering;

/// <summary>
/// Grass and flowers of a planet: a <see cref="VoxelInstancer"/> under the terrain node that scatters the meshes of <c>flora.glb</c>
/// (<c>tools/art/planet_flora.py</c>) over the rendered terrain meshes. Purely visual and not persistent: no collisions, nothing is saved,
/// and Voxel Tools removes the instances on edited ground by itself. The trees, bushes, rocks, boulders and crystals are gameplay objects of
/// the generator (K6-31) and are drawn by <see cref="Horyzont.Game.Planet.Props.PropRenderer"/>; this class only loads their meshes.
/// </summary>
public static class PlanetFlora
{
    /// <summary>Imported meshes, one top-level mesh object per item.</summary>
    public const string ModelPath = "res://assets/planet/flora.glb";

    /// <summary>Shader that sways plants in the wind.</summary>
    public const string FoliageShaderPath = "res://shaders/foliage.gdshader";

    // Wind sway (metres at the given height above the ground) of the items that move; rocks and crystals stay still.
    private static readonly Dictionary<string, (float Sway, float Height)> _wind = new(StringComparer.Ordinal)
    {
        ["Pine"] = (0.25f, 9f),
        ["PineTall"] = (0.35f, 13f),
        ["Broadleaf"] = (0.25f, 8f),
        ["BroadleafAutumn"] = (0.25f, 7f),
        ["Bush"] = (0.05f, 1.3f),
        ["Grass"] = (0.08f, 0.45f),
        ["GrassFlowersYellow"] = (0.08f, 0.45f),
        ["GrassFlowersPink"] = (0.08f, 0.45f),
        ["GrassFlowersBlue"] = (0.08f, 0.45f),
    };

    private enum Ground
    {
        // Any terrain material: also coarse LOD meshes, whose material is the stone below the thin dirt layer.
        Any,

        // Only the natural surface material (LOD 0 meshes are exact enough for that).
        Dirt,
    }

    /// <summary>Creates the instancer for a planet whose centre is the terrain origin. Main thread, outside the tree.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The instancer to add as a child of the terrain node.</returns>
    public static VoxelInstancer Create(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var meshes = LoadMeshes();
        var radius = planet.RadiusMeters;
        var land = radius + PlanetLook.SeaLevelMeters + PlanetLook.BeachHeightMeters;
        var library = new VoxelInstanceLibrary();
        var id = 0;

        void Add(string mesh, int lod, float density, float maxSlope, float minHeight, float maxHeight, float minScale, float maxScale,
            float alignment, Ground ground, float noiseFrequency = 0f, float noiseThreshold = 0f, bool shadows = true, bool snap = false,
            float offset = 0f)
        {
            var generator = new VoxelInstanceGenerator
            {
                EmitMode = VoxelInstanceGenerator.EmitModeEnum.FromFaces,
                Density = density,
                MinScale = minScale,
                MaxScale = maxScale,
                ScaleDistribution = VoxelInstanceGenerator.Distribution.Quadratic,
                VerticalAlignment = alignment,
                MaxSlopeDegrees = maxSlope,
                MaxSlopeFalloffDegrees = 5f,
                MinHeight = minHeight,
                MaxHeight = maxHeight,
                MinHeightFalloff = 2f,
                MaxHeightFalloff = 10f,
                RandomRotation = true,
                OffsetAlongNormal = offset,
                SnapToGeneratorSdfEnabled = snap,
            };
            if (ground == Ground.Dirt)
            {
                generator.VoxelTextureFilterEnabled = true;
                generator.VoxelTextureFilterArray = [TerrainMaterials.DirtTextureIndex];
            }

            if (noiseFrequency > 0f)
            {
                generator.Noise = new FastNoiseLite
                {
                    Seed = (int)(planet.Seed & 0x7FFFFFFF) + id,
                    NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
                    Frequency = noiseFrequency,
                    FractalOctaves = 2,
                };
                generator.NoiseDimension = VoxelInstanceGenerator.Dimension.Dimension3D;
                generator.NoiseThreshold = noiseThreshold;
            }

            var item = new VoxelInstanceLibraryMultiMeshItem
            {
                LodIndex = lod,
                Generator = generator,
                Persistent = false,
                Name = mesh,
            };
            item.SetMesh(meshes[mesh], 0);
            item.SetCastShadowsSetting(shadows ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off);
            library.AddItem(id++, item);
        }

        var trees = radius + PlanetLook.TreeLineMeters;
        var snow = radius + PlanetLook.SnowLineMeters;

        // Trees, bushes, rocks, boulders and crystals are gameplay objects since K6-31: PropRenderer draws them from the frozen layout V1, so
        // they are not scattered here (a second, unrelated set would show props that cannot be hit). Only the ground cover stays visual.
        Add("Grass", 0, 0.9f, 32f, land, snow - 10f, 0.7f, 1.4f, 0.6f, Ground.Dirt, 1f / 40f, 0.3f, shadows: false);
        Add("GrassFlowersYellow", 0, 0.05f, 25f, land + 1f, trees, 0.8f, 1.2f, 0.7f, Ground.Dirt, 1f / 30f, -0.2f, shadows: false);
        Add("GrassFlowersPink", 0, 0.04f, 25f, land + 1f, trees, 0.8f, 1.2f, 0.7f, Ground.Dirt, 1f / 25f, -0.3f, shadows: false);
        Add("GrassFlowersBlue", 0, 0.04f, 25f, land + 10f, snow, 0.8f, 1.2f, 0.7f, Ground.Dirt, 1f / 35f, -0.3f, shadows: false);

        return new VoxelInstancer
        {
            Name = "Flora",
            Library = library,
            UpMode = VoxelInstancer.UpModeEnum.Sphere,
            FadingEnabled = true,
            FadingDuration = 0.4f,
        };
    }

    /// <summary>Loads the meshes of <see cref="ModelPath"/> by item name, with the wind shader on the plants that sway.</summary>
    /// <returns>The meshes; the props of the layout V1 draw them too (K6-31 D20).</returns>
    internal static Dictionary<string, Mesh> LoadMeshes()
    {
        var scene = GD.Load<PackedScene>(ModelPath) ?? throw new InvalidOperationException($"Model {ModelPath} not found.");
        var root = scene.Instantiate<Node>();
        try
        {
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            foreach (var child in root.GetChildren())
            {
                if (child is MeshInstance3D { Mesh: { } mesh } instance)
                {
                    string name = instance.Name;
                    if (_wind.TryGetValue(name, out var wind))
                    {
                        ApplyWind(mesh, wind.Sway, wind.Height);
                    }

                    meshes[name] = mesh;
                }
            }

            return meshes;
        }
        finally
        {
            root.Free();
        }
    }

    private static void ApplyWind(Mesh mesh, float sway, float height)
    {
        var shader = GD.Load<Shader>(FoliageShaderPath) ?? throw new InvalidOperationException($"Shader {FoliageShaderPath} not found.");
        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.SurfaceGetMaterial(surface) is not BaseMaterial3D imported)
            {
                continue;
            }

            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("u_albedo", imported.AlbedoColor);
            material.SetShaderParameter("u_roughness", imported.Roughness);
            material.SetShaderParameter("u_emission", imported.Emission);
            material.SetShaderParameter("u_emission_energy", imported.EmissionEnabled ? imported.EmissionEnergyMultiplier : 0f);
            material.SetShaderParameter("u_sway", sway);
            material.SetShaderParameter("u_height", height);
            mesh.SurfaceSetMaterial(surface, material);
        }
    }
}
