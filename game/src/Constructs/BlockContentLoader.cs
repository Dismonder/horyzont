using Horyzont.Constructs.Content;

namespace Horyzont.Game.Constructs;

/// <summary>Loads the block content (<c>blocks.json</c>) at start. Invalid content is a build error: the exception stops the start.</summary>
public static class BlockContentLoader
{
    /// <summary>Path of the content file inside the project.</summary>
    public const string DefaultPath = "res://content/blocks/blocks.json";

    /// <summary>Parses the content file.</summary>
    /// <param name="path">Resource path of the JSON file.</param>
    /// <param name="meshes">Resolver for mesh references; defaults to <see cref="GodotShapeMeshResolver"/>.</param>
    /// <returns>The catalog: block types for <c>WorldState.CreateNew</c>, render materials and prefab meshes.</returns>
    /// <exception cref="InvalidDataException">The file is missing or invalid (the message names the JSON path).</exception>
    public static BlockTypeCatalog Load(string path = DefaultPath, IShapeMeshResolver? meshes = null)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidDataException($"Block content '{path}' is missing or empty ({Godot.FileAccess.GetOpenError()}).");
        }

        return BlockTypeCatalog.Parse(bytes, meshes ?? new GodotShapeMeshResolver());
    }
}
