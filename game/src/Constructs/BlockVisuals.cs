using Godot;

using Horyzont.Constructs.Content;

namespace Horyzont.Game.Constructs;

/// <summary>
/// One <see cref="Texture2DArray"/> (a layer per block material: <c>Id - 1</c>) and one <see cref="ShaderMaterial"/> for the whole
/// construct world. In K2 a material is a colour; textures come with K8-50.
/// </summary>
public sealed class BlockVisuals : IDisposable
{
    /// <summary>Resource path of the block shader.</summary>
    public const string ShaderPath = "res://shaders/blocks.gdshader";

    private const int TextureSize = 16;

    private BlockVisuals(Texture2DArray textures, ShaderMaterial material)
    {
        Textures = textures;
        Material = material;
    }

    /// <summary>Texture array of the block materials.</summary>
    public Texture2DArray Textures { get; }

    /// <summary>Material shared by every chunk surface.</summary>
    public ShaderMaterial Material { get; }

    /// <summary>Builds the textures and the material from the content.</summary>
    /// <param name="catalog">Block content.</param>
    /// <returns>The visuals.</returns>
    public static BlockVisuals Create(BlockTypeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var images = new Godot.Collections.Array<Image>();
        foreach (var material in catalog.Materials)
        {
            images.Add(CreateLayer(material));
        }

        var textures = new Texture2DArray();
        var error = textures.CreateFromImages(images);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Cannot create the block texture array: {error}.");
        }

        var shader = ResourceLoader.Load<Shader>(ShaderPath) ?? throw new InvalidDataException($"Shader '{ShaderPath}' is missing.");
        var shaderMaterial = new ShaderMaterial { Shader = shader };
        shaderMaterial.SetShaderParameter("blocks", textures);
        return new BlockVisuals(textures, shaderMaterial);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Material.Dispose();
        Textures.Dispose();
    }

    private static Image CreateLayer(BlockMaterialDef material)
    {
        var image = Image.CreateEmpty(TextureSize, TextureSize, false, Image.Format.Rgba8);
        if (material.Id == BlockTypeCatalog.MissingMaterialId)
        {
            // Magenta-black checkerboard: an unmistakable "missing block".
            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    image.SetPixel(x, y, ((x / 4) + (y / 4)) % 2 == 0 ? new Color(1, 0, 1) : new Color(0, 0, 0));
                }
            }

            return image;
        }

        var color = new Color(
            ((material.ColorRgba8 >> 24) & 0xFF) / 255f,
            ((material.ColorRgba8 >> 16) & 0xFF) / 255f,
            ((material.ColorRgba8 >> 8) & 0xFF) / 255f,
            (material.ColorRgba8 & 0xFF) / 255f);
        var edge = new Color(color.R * 0.8f, color.G * 0.8f, color.B * 0.8f, color.A);
        for (var y = 0; y < TextureSize; y++)
        {
            for (var x = 0; x < TextureSize; x++)
            {
                var onEdge = x == 0 || y == 0;
                image.SetPixel(x, y, onEdge ? edge : color);
            }
        }

        return image;
    }
}
