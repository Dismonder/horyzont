using Horyzont.Core.Content;

namespace Horyzont.Game.Content;

/// <summary>Loads the survival content (items, terrain yields, start kit, props) from <c>res://content/</c> at start (K6-29W D16).</summary>
public static class SurvivalContentLoader
{
    /// <summary>Content root inside the project.</summary>
    public const string DefaultRoot = "res://content/";

    /// <summary>Reads and validates the five content files (items, terrain yields, start kit, K6-31 props, K6-32 recipes). Invalid content is a build error: the exception stops the start.</summary>
    /// <param name="root">Resource path of the content directory, ending with a slash.</param>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidDataException">A file is missing, empty or invalid (the message names the file and the JSON path).</exception>
    public static SurvivalContent Load(string root = DefaultRoot) => ContentLoader.Parse(
        ReadText(root + ContentLoader.ItemsFile), ReadText(root + ContentLoader.YieldsFile), ReadText(root + ContentLoader.StartFile),
        ReadText(root + ContentLoader.PropsFile), ReadText(root + ContentLoader.RecipesFile));

    /// <summary>The item type keys of the content in file order, for the world registry.</summary>
    /// <param name="content">The content.</param>
    /// <returns>The keys.</returns>
    public static IReadOnlyList<string> ItemKeys(SurvivalContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Items.Items.Select(i => i.Key).ToArray();
    }

    private static string ReadText(string path)
    {
        var text = Godot.FileAccess.GetFileAsString(path);
        return string.IsNullOrEmpty(text)
            ? throw new InvalidDataException($"Survival content '{path}' is missing or empty ({Godot.FileAccess.GetOpenError()}).")
            : text;
    }
}
