using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Rendering;

/// <summary>
/// Render material of planet terrain: <c>planet_terrain.gdshader</c> with a texture array generated in code from seamless noise. The
/// first layers follow the <see cref="TerrainMaterials"/> texture indices stored in voxels; the extra layers (grass, snow, cliff,
/// meadow) are painted by the shader from height and slope only, so they never reach the terrain data. Create the base material
/// once and share it; <see cref="ForPlanet"/> gives each planet its own copy with the planet uniforms.
/// </summary>
public static class PlanetTerrainMaterial
{
    public const string ShaderPath = "res://shaders/planet_terrain.gdshader";
    public const int TextureSize = 256;

    /// <summary>Layer of grass painted on gentle dirt slopes.</summary>
    public const int GrassLayer = 5;

    /// <summary>Layer of snow painted above the snow line.</summary>
    public const int SnowLayer = 6;

    /// <summary>Layer of bare rock painted on steep slopes.</summary>
    public const int CliffLayer = 7;

    /// <summary>Layer of dry meadow mixed into the grass by a large-scale noise.</summary>
    public const int MeadowLayer = 8;

    /// <summary>Number of layers in the texture array.</summary>
    public const int LayerCount = 9;

    // Flat colours of the stored materials (stone, dirt, sand, ore, bedrock) for the impostor vertex colours; the painted layers are not in the data.
    private static readonly Color[] _baseColors =
    [
        new(0.46f, 0.46f, 0.48f),
        new(0.38f, 0.26f, 0.15f),
        new(0.82f, 0.74f, 0.50f),
        new(0.52f, 0.47f, 0.40f),
        new(0.14f, 0.14f, 0.17f),
    ];

    private const int Seed = 1137;

    /// <summary>The base colour of a texture index (impostor vertex colours, K5-22 decision 12); grey for an unknown index.</summary>
    /// <param name="textureIndex">The texture index (<see cref="TerrainMaterials"/>).</param>
    /// <returns>The colour.</returns>
    public static Color BaseColor(int textureIndex) =>
        textureIndex >= 0 && textureIndex < _baseColors.Length ? _baseColors[textureIndex] : new Color(0.5f, 0.5f, 0.5f);

    /// <summary>Builds the shared base material. Main thread.</summary>
    public static ShaderMaterial Create()
    {
        var shader = GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"Shader {ShaderPath} not found.");
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("u_textures", CreateTextureArray());
        material.SetShaderParameter("u_sea_level", PlanetLook.SeaLevelMeters);
        material.SetShaderParameter("u_beach_height", PlanetLook.BeachHeightMeters);
        material.SetShaderParameter("u_snow_line", PlanetLook.SnowLineMeters);
        return material;
    }

    /// <summary>A copy of <paramref name="shared"/> (textures shared) with the radius of a planet whose centre is the terrain origin.</summary>
    /// <param name="shared">The material from <see cref="Create"/>.</param>
    /// <param name="planet">The planet.</param>
    /// <returns>The material of the planet.</returns>
    public static Material ForPlanet(Material shared, PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(planet);
        if (shared is not ShaderMaterial shaderMaterial)
        {
            return shared;
        }

        var copy = (ShaderMaterial)shaderMaterial.Duplicate();
        copy.SetShaderParameter("u_planet_radius", planet.RadiusMeters);
        return copy;
    }

    /// <summary>Layer images in texture index order, then the painted layers.</summary>
    public static Texture2DArray CreateTextureArray()
    {
        var images = new Godot.Collections.Array<Image>();
        for (var index = 0; index < LayerCount; index++)
        {
            images.Add(CreateImage(index));
        }

        var array = new Texture2DArray();
        var error = array.CreateFromImages(images);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Creating the terrain texture array failed: {error}.");
        }

        return array;
    }

    private static Image CreateImage(int index)
    {
        // Seamless greyscale noise fields (native, fast), combined per layer into an RGB texture.
        var broad = Field(index * 7 + 1, FastNoiseLite.NoiseTypeEnum.Perlin, 0.012f, 4, FastNoiseLite.FractalTypeEnum.Fbm);
        var fine = Field(index * 7 + 2, FastNoiseLite.NoiseTypeEnum.Perlin, 0.06f, 3, FastNoiseLite.FractalTypeEnum.Fbm);
        var cells = Field(index * 7 + 3, FastNoiseLite.NoiseTypeEnum.Cellular, 0.03f, 1, FastNoiseLite.FractalTypeEnum.None);
        var grain = Field(index * 7 + 4, FastNoiseLite.NoiseTypeEnum.Value, 0.5f, 1, FastNoiseLite.FractalTypeEnum.None);

        var pixels = new byte[TextureSize * TextureSize * 3];
        for (var i = 0; i < TextureSize * TextureSize; i++)
        {
            var b = broad[i] / 255f;
            var f = fine[i] / 255f;
            var c = cells[i] / 255f;
            var g = grain[i] / 255f;
            var color = Shade(index, b, f, c, g);
            pixels[i * 3] = ToByte(color.R);
            pixels[(i * 3) + 1] = ToByte(color.G);
            pixels[(i * 3) + 2] = ToByte(color.B);
        }

        var image = Image.CreateFromData(TextureSize, TextureSize, false, Image.Format.Rgb8, pixels);
        image.GenerateMipmaps();
        return image;
    }

    private static Color Shade(int index, float broad, float fine, float cells, float grain)
    {
        // Placeholder art direction: stylized, slightly saturated natural colours; values in sRGB.
        switch (index)
        {
            case TerrainMaterials.StoneTextureIndex:
                {
                    var crack = Mathf.SmoothStep(0.05f, 0.0f, cells) * 0.35f;
                    return Mix(new Color(0.40f, 0.40f, 0.42f), new Color(0.56f, 0.55f, 0.53f), (broad * 0.7f) + (fine * 0.3f)).Darkened(crack);
                }

            case TerrainMaterials.DirtTextureIndex:
                {
                    var pebble = grain > 0.93f ? 0.25f : 0f;
                    return Mix(new Color(0.30f, 0.20f, 0.12f), new Color(0.46f, 0.33f, 0.20f), (broad * 0.6f) + (fine * 0.4f)).Lightened(pebble);
                }

            case TerrainMaterials.SandTextureIndex:
                return Mix(new Color(0.76f, 0.67f, 0.47f), new Color(0.90f, 0.82f, 0.62f), (fine * 0.5f) + (grain * 0.3f) + (broad * 0.2f));

            case TerrainMaterials.OreTextureIndex:
                {
                    var rock = Mix(new Color(0.38f, 0.36f, 0.36f), new Color(0.50f, 0.47f, 0.44f), broad);
                    return grain > 0.86f || cells < 0.06f ? Mix(new Color(0.85f, 0.48f, 0.16f), new Color(0.98f, 0.72f, 0.30f), fine) : rock;
                }

            case TerrainMaterials.CoreTextureIndex:
                {
                    var glint = grain > 0.97f ? 0.35f : 0f;
                    return Mix(new Color(0.10f, 0.09f, 0.13f), new Color(0.20f, 0.17f, 0.26f), broad).Lightened(glint);
                }

            case GrassLayer:
                {
                    var blades = (grain * 0.55f) + (fine * 0.45f);
                    return Mix(new Color(0.20f, 0.34f, 0.11f), new Color(0.36f, 0.52f, 0.18f), (blades * 0.7f) + (broad * 0.3f));
                }

            case SnowLayer:
                return Mix(new Color(0.84f, 0.88f, 0.95f), new Color(0.97f, 0.98f, 1.0f), (broad * 0.6f) + (grain * 0.4f));

            case CliffLayer:
                {
                    var crack = Mathf.SmoothStep(0.06f, 0.0f, cells) * 0.4f;
                    var band = (Mathf.Sin(broad * 18f) * 0.5f) + 0.5f;
                    return Mix(new Color(0.36f, 0.33f, 0.30f), new Color(0.55f, 0.50f, 0.44f), (band * 0.4f) + (fine * 0.6f)).Darkened(crack);
                }

            case MeadowLayer:
                return Mix(new Color(0.42f, 0.42f, 0.17f), new Color(0.60f, 0.55f, 0.26f), (grain * 0.5f) + (fine * 0.5f));

            default:
                throw new ArgumentOutOfRangeException(nameof(index), index, "Unknown terrain layer.");
        }
    }

    private static byte[] Field(int seed, FastNoiseLite.NoiseTypeEnum type, float frequency, int octaves, FastNoiseLite.FractalTypeEnum fractal)
    {
        using var noise = new FastNoiseLite
        {
            Seed = Seed + seed,
            NoiseType = type,
            Frequency = frequency,
            FractalType = fractal,
            FractalOctaves = octaves,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub,
        };
        var image = noise.GetSeamlessImage(TextureSize, TextureSize, false, false, 0.1, true);
        image.Convert(Image.Format.L8);
        return image.GetData();
    }

    private static Color Mix(Color a, Color b, float t) => a.Lerp(b, Mathf.Clamp(t, 0f, 1f));

    private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
}
