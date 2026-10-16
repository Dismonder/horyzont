using System.Text;
using System.Text.Json;

using Horyzont.Core.Items;
using Horyzont.Core.Protocol;

namespace Horyzont.Core.Content;

/// <summary>
/// Reads and validates the survival content (D16 of K6-29W): <c>items/items.json</c>, <c>terrain/yields.json</c> and <c>start.json</c>.
/// Every error is an <see cref="InvalidDataException"/> naming the file and the JSON path. Unknown properties (e.g. <c>visual</c>) are ignored.
/// </summary>
public static class ContentLoader
{
    /// <summary>Items file relative to the content root.</summary>
    public const string ItemsFile = "items/items.json";

    /// <summary>Terrain yields file relative to the content root.</summary>
    public const string YieldsFile = "terrain/yields.json";

    /// <summary>Start kit file relative to the content root.</summary>
    public const string StartFile = "start.json";

    /// <summary>Props file relative to the content root (K6-31 D8).</summary>
    public const string PropsFile = "props/props.json";

    private const int MaxStackLimit = 999;

    private static readonly JsonDocumentOptions _options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = false };

    /// <summary>Reads the three files from a content root directory.</summary>
    /// <param name="contentRoot">Directory with <see cref="ItemsFile"/>, <see cref="YieldsFile"/> and <see cref="StartFile"/>.</param>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidDataException">The content is invalid.</exception>
    public static SurvivalContent LoadDirectory(string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        return Parse(
            File.ReadAllText(Path.Combine(contentRoot, ItemsFile)),
            File.ReadAllText(Path.Combine(contentRoot, YieldsFile)),
            File.ReadAllText(Path.Combine(contentRoot, StartFile)));
    }

    /// <summary>Parses and validates the content.</summary>
    /// <param name="itemsJson">Text of <see cref="ItemsFile"/>.</param>
    /// <param name="yieldsJson">Text of <see cref="YieldsFile"/>.</param>
    /// <param name="startJson">Text of <see cref="StartFile"/>.</param>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidDataException">The content is invalid.</exception>
    public static SurvivalContent Parse(string itemsJson, string yieldsJson, string startJson)
    {
        ArgumentNullException.ThrowIfNull(itemsJson);
        ArgumentNullException.ThrowIfNull(yieldsJson);
        ArgumentNullException.ThrowIfNull(startJson);
        ItemDef[] items;
        using (var document = Open(itemsJson, ItemsFile))
        {
            items = ParseItems(document.RootElement);
        }

        var catalog = new ItemCatalog(items);
        TerrainYieldDef[] yields;
        using (var document = Open(yieldsJson, YieldsFile))
        {
            yields = ParseYields(document.RootElement, catalog);
        }

        StartKitDef startKit;
        using (var document = Open(startJson, StartFile))
        {
            startKit = ParseStart(document.RootElement, catalog);
        }

        for (var i = 0; i < items.Length; i++)
        {
            var key = items[i].Key;
            if ((items[i].Flags & ItemFlags.Retired) == 0 && !HasSource(key, yields, startKit))
            {
                throw Invalid(ItemsFile, $"$.items[{i}]", $"item '{key}' has no source (a terrain yield or the start kit)");
            }
        }

        return new SurvivalContent(catalog, yields, startKit);
    }

    /// <summary>Parses and validates the content with the harvestable props (K6-31 D8).</summary>
    /// <param name="itemsJson">Text of <see cref="ItemsFile"/>.</param>
    /// <param name="yieldsJson">Text of <see cref="YieldsFile"/>.</param>
    /// <param name="startJson">Text of <see cref="StartFile"/>.</param>
    /// <param name="propsJson">Text of <see cref="PropsFile"/>.</param>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidDataException">The content is invalid.</exception>
    public static SurvivalContent Parse(string itemsJson, string yieldsJson, string startJson, string propsJson)
    {
        ArgumentNullException.ThrowIfNull(propsJson);
        return Parse(itemsJson, yieldsJson, startJson); // K6-31 B3: props, their validation and HasSource.
    }

    private static bool HasSource(string key, TerrainYieldDef[] yields, StartKitDef startKit)
    {
        foreach (var yield in yields)
        {
            if (yield.ItemKey == key)
            {
                return true;
            }
        }

        foreach (var entry in startKit.Entries)
        {
            if (entry.ItemKey == key)
            {
                return true;
            }
        }

        return false;
    }

    private static ItemDef[] ParseItems(JsonElement root)
    {
        var array = RequiredArray(root, "items", "$", ItemsFile);
        var items = new List<ItemDef>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var path = $"$.items[{index}]";
            RequireObject(element, path, ItemsFile);
            var key = RequiredKey(element, "key", path, ItemsFile);
            if (!keys.Add(key))
            {
                throw Invalid(ItemsFile, path + ".key", $"duplicate key '{key}'");
            }

            var category = RequiredEnum<ItemCategory>(element, "category", path, ItemsFile);
            var flags = ItemFlags.None;
            if (element.TryGetProperty("flags", out var flagArray))
            {
                if (flagArray.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid(ItemsFile, path + ".flags", "expected an array of flag names");
                }

                var flagIndex = 0;
                foreach (var flag in flagArray.EnumerateArray())
                {
                    var flagPath = $"{path}.flags[{flagIndex}]";
                    if (flag.ValueKind != JsonValueKind.String || !TryParseName<ItemFlags>(flag.GetString()!, out var value) || value == ItemFlags.None)
                    {
                        throw Invalid(ItemsFile, flagPath, "unknown flag");
                    }

                    flags |= value;
                    flagIndex++;
                }
            }

            var defaultStack = ItemCatalog.DefaultMaxStack(category);
            var maxStack = defaultStack;
            if (element.TryGetProperty("maxStack", out var stackElement))
            {
                if (stackElement.ValueKind != JsonValueKind.Number || !stackElement.TryGetInt32(out var stack) || stack < 1 || stack > MaxStackLimit)
                {
                    throw Invalid(ItemsFile, path + ".maxStack", $"must be an integer in 1..{MaxStackLimit}");
                }

                var overridden = element.TryGetProperty("maxStackOverride", out var overrideElement) && overrideElement.ValueKind == JsonValueKind.True;
                if (stack != defaultStack && !overridden)
                {
                    throw Invalid(ItemsFile, path + ".maxStack", $"{stack} differs from {defaultStack} of category {category}; set \"maxStackOverride\": true");
                }

                maxStack = (ushort)stack;
            }

            var toolTier = OptionalInt(element, "toolTier", path, ItemsFile, 0, byte.MaxValue, 0);
            var slot = EquipmentSlot.None;
            if (element.TryGetProperty("equipmentSlot", out _))
            {
                slot = RequiredEnum<EquipmentSlot>(element, "equipmentSlot", path, ItemsFile);
            }

            items.Add(new ItemDef(key, category, maxStack, flags, (byte)toolTier, slot));
            index++;
        }

        if (items.Count > ProtocolConstants.MaxItemTypes)
        {
            throw Invalid(ItemsFile, "$.items", $"more than {ProtocolConstants.MaxItemTypes} items");
        }

        return [.. items];
    }

    private static TerrainYieldDef[] ParseYields(JsonElement root, ItemCatalog catalog)
    {
        var array = RequiredArray(root, "yields", "$", YieldsFile);
        var yields = new List<TerrainYieldDef>();
        var materials = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var path = $"$.yields[{index}]";
            RequireObject(element, path, YieldsFile);
            var material = RequiredKey(element, "material", path, YieldsFile);
            if (!materials.Add(material))
            {
                throw Invalid(YieldsFile, path + ".material", $"duplicate material '{material}'");
            }

            var item = RequiredKey(element, "item", path, YieldsFile);
            if (!catalog.TryGet(item, out _))
            {
                throw Invalid(YieldsFile, path + ".item", $"unknown item '{item}'");
            }

            if (!element.TryGetProperty("itemsPerCubicMeter", out var rate) || rate.ValueKind != JsonValueKind.Number
                || !rate.TryGetDouble(out var perCubicMeter) || !double.IsFinite(perCubicMeter) || perCubicMeter <= 0)
            {
                throw Invalid(YieldsFile, path + ".itemsPerCubicMeter", "must be a positive number");
            }

            var toolTier = OptionalInt(element, "toolTier", path, YieldsFile, 0, byte.MaxValue, 0);
            yields.Add(new TerrainYieldDef(material, item, perCubicMeter, (byte)toolTier));
            index++;
        }

        return [.. yields];
    }

    private static StartKitDef ParseStart(JsonElement root, ItemCatalog catalog)
    {
        RequireObject(root, "$", StartFile);
        if (!root.TryGetProperty("containers", out var containers) || containers.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(StartFile, "$.containers", "expected an object with playerBag, hotbar and equipment");
        }

        var bag = (ushort)RequiredInt(containers, "playerBag", "$.containers", StartFile, 1, ProtocolConstants.MaxContainerSlots);
        var hotbar = (ushort)RequiredInt(containers, "hotbar", "$.containers", StartFile, 1, ProtocolConstants.MaxContainerSlots);
        var equipment = (ushort)RequiredInt(containers, "equipment", "$.containers", StartFile, 1, ProtocolConstants.MaxContainerSlots);
        var array = RequiredArray(root, "items", "$", StartFile);
        var entries = new List<StartKitEntry>();
        var used = new HashSet<(ContainerKind, int)>();
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var path = $"$.items[{index}]";
            RequireObject(element, path, StartFile);
            var key = RequiredKey(element, "item", path, StartFile);
            if (!catalog.TryGet(key, out var item))
            {
                throw Invalid(StartFile, path + ".item", $"unknown item '{key}'");
            }

            var count = RequiredInt(element, "count", path, StartFile, 1, item.MaxStack);
            var container = RequiredEnum<ContainerKind>(element, "container", path, StartFile);
            var size = container switch
            {
                ContainerKind.PlayerBag => bag,
                ContainerKind.Hotbar => hotbar,
                ContainerKind.Equipment => equipment,
                _ => throw Invalid(StartFile, path + ".container", "must be playerBag, hotbar or equipment"),
            };
            var slot = RequiredInt(element, "slot", path, StartFile, 0, size - 1);
            if (!used.Add((container, slot)))
            {
                throw Invalid(StartFile, path + ".slot", "slot already used by another start item");
            }

            entries.Add(new StartKitEntry(key, (ushort)count, container, (ushort)slot));
            index++;
        }

        return new StartKitDef(bag, hotbar, equipment, entries);
    }

    private static JsonDocument Open(string json, string file)
    {
        try
        {
            return JsonDocument.Parse(json, _options);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{file}: invalid JSON: {e.Message}", e);
        }
    }

    private static void RequireObject(JsonElement element, string path, string file)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(file, path, "expected an object");
        }
    }

    private static JsonElement RequiredArray(JsonElement parent, string name, string path, string file)
    {
        RequireObject(parent, path, file);
        if (!parent.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(file, $"{path}.{name}", "expected an array");
        }

        return array;
    }

    private static string RequiredKey(JsonElement parent, string name, string path, string file)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(file, $"{path}.{name}", "expected a string");
        }

        var text = value.GetString()!;
        if (text.Length == 0 || Encoding.UTF8.GetByteCount(text) > ProtocolConstants.MaxRegistryKeyBytes)
        {
            throw Invalid(file, $"{path}.{name}", $"a key has 1..{ProtocolConstants.MaxRegistryKeyBytes} UTF-8 bytes");
        }

        return text;
    }

    private static int RequiredInt(JsonElement parent, string name, string path, string file, int min, int max)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)
            || number < min || number > max)
        {
            throw Invalid(file, $"{path}.{name}", $"must be an integer in {min}..{max}");
        }

        return number;
    }

    private static int OptionalInt(JsonElement parent, string name, string path, string file, int min, int max, int fallback) =>
        parent.TryGetProperty(name, out _) ? RequiredInt(parent, name, path, file, min, max) : fallback;

    private static T RequiredEnum<T>(JsonElement parent, string name, string path, string file)
        where T : struct, Enum
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || !TryParseName<T>(value.GetString()!, out var parsed))
        {
            throw Invalid(file, $"{path}.{name}", $"expected one of {string.Join(", ", Enum.GetNames<T>())}");
        }

        return parsed;
    }

    // Names only (case-insensitive), never numbers, so "1" is not a valid flag or category.
    private static bool TryParseName<T>(string text, out T value)
        where T : struct, Enum
    {
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                value = Enum.Parse<T>(name);
                return true;
            }
        }

        value = default;
        return false;
    }

    private static InvalidDataException Invalid(string file, string path, string problem) => new($"{file}: {path}: {problem}");
}
