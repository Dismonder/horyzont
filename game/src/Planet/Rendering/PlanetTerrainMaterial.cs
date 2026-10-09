using Godot;

namespace Horyzont.Game.Planet.Rendering;

/// <summary>
/// Render material of planet terrain: <c>planet_terrain.gdshader</c> with a texture array of placeholder textures generated in code
/// (one layer per <see cref="TerrainMaterials"/> texture index). Create it once and share it between planets.
/// </summary>
public static class PlanetTerrainMaterial
{
    public const string ShaderPath = "res://shaders/planet_terrain.gdshader";
    public const int TextureSize = 64;

    // Base colours by texture index (stone, dirt, sand, ore, bedrock).
    private static readonly Color[] _baseColors =
    [
        new(0.46f, 0.46f, 0.48f),
        new(0.38f, 0.26f, 0.15f),
        new(0.82f, 0.74f, 0.50f),
        new(0.52f, 0.47f, 0.40f),
        new(0.14f, 0.14f, 0.17f),
    ];

    /// <summary>Builds the material. Main thread.</summary>
    public static ShaderMaterial Create()
    {
        var shader = GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"Shader {ShaderPath} not found.");
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("u_textures", CreateTextureArray());
        return material;
    }

    /// <summary>Layer images in texture index order.</summary>
    public static Texture2DArray CreateTextureArray()
    {
        var images = new Godot.Collections.Array<Image>();
        for (var index = 0; index < _baseColors.Length; index++)
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
        var image = Image.CreateEmpty(TextureSize, TextureSize, true, Image.Format.Rgb8);
        var baseColor = _baseColors[index];
        for (var y = 0; y < TextureSize; y++)
        {
            for (var x = 0; x < TextureSize; x++)
            {
                // Cheap deterministic grain: placeholder art, not part of any format.
                var hash = unchecked(((uint)((x * 73856093) ^ (y * 19349663) ^ (index * 83492791))) * 2654435761u) >> 24;
                var shade = 0.88f + (0.24f * (hash / 255f));
                var color = new Color(baseColor.R * shade, baseColor.G * shade, baseColor.B * shade);
                if (index == TerrainMaterials.OreTextureIndex && hash > 232)
                {
                    color = new Color(0.9f, 0.6f, 0.15f);
                }

                image.SetPixel(x, y, color);
            }
        }

        image.GenerateMipmaps();
        return image;
    }
}
