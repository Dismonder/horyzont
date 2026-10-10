using System.Text.Json;

using Godot;

using Horyzont.Client.Inventory;
using Horyzont.Client.Tools;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The <c>visual</c> fields of <c>items.json</c> (glyph, colour, <c>clientTool</c>): only the game reads them, the Core content loader
/// skips them (K6-29W D16).
/// </summary>
public sealed class ItemVisuals
{
    /// <summary>Resource path of the items file.</summary>
    public const string DefaultPath = "res://content/items/items.json";

    private static readonly ItemVisual _unknown = new("?", new Color(0.45f, 0.5f, 0.55f), ToolKind.None);

    private readonly Dictionary<string, ItemVisual> _byKey;

    private ItemVisuals(Dictionary<string, ItemVisual> byKey) => _byKey = byKey;

    /// <summary>Reads the items file of the project.</summary>
    /// <param name="path">Resource path.</param>
    /// <returns>The visuals.</returns>
    /// <exception cref="InvalidDataException">The file is missing or not valid JSON.</exception>
    public static ItemVisuals Load(string path = DefaultPath)
    {
        var text = Godot.FileAccess.GetFileAsString(path);
        return string.IsNullOrEmpty(text) ? throw new InvalidDataException($"Items file '{path}' is missing or empty.") : Parse(text);
    }

    /// <summary>Parses the items file.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The visuals.</returns>
    /// <exception cref="InvalidDataException">The text is not an items file.</exception>
    public static ItemVisuals Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var map = new Dictionary<string, ItemVisual>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
            {
                var key = item.GetProperty("key").GetString() ?? throw new InvalidDataException("An item has no key.");
                var glyph = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
                glyph = glyph.Length > 2 ? glyph[..2] : glyph;
                var color = _unknown.Color;
                var tool = ToolKind.None;
                if (item.TryGetProperty("visual", out var visual))
                {
                    if (visual.TryGetProperty("glyph", out var g) && g.GetString() is { Length: > 0 } text)
                    {
                        glyph = text;
                    }

                    if (visual.TryGetProperty("color", out var c) && c.GetString() is { } html && Color.HtmlIsValid(html))
                    {
                        color = Color.FromHtml(html);
                    }

                    if (visual.TryGetProperty("clientTool", out var t))
                    {
                        tool = t.GetString() switch
                        {
                            "terrain" => ToolKind.TerrainManipulator,
                            "builder" => ToolKind.Builder,
                            _ => ToolKind.None,
                        };
                    }
                }

                map[key] = new ItemVisual(glyph, color, tool);
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("The items file has no valid 'items' list.", e);
        }

        return new ItemVisuals(map);
    }

    /// <summary>The visual of an item.</summary>
    /// <param name="key">Item key, or null.</param>
    /// <returns>The visual, a grey question mark for an unknown key.</returns>
    public ItemVisual Get(string? key) => key is not null && _byKey.TryGetValue(key, out var visual) ? visual : _unknown;

    /// <summary>The tool map of the items that select a tool.</summary>
    /// <returns>The map.</returns>
    public ItemToolMap ToolMap() => new(_byKey.Where(p => p.Value.Tool != ToolKind.None).ToDictionary(p => p.Key, p => p.Value.Tool, StringComparer.Ordinal));
}
