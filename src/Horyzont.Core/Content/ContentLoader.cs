using System.Text;
using System.Text.Json;

using Horyzont.Core.Crafting;
using Horyzont.Core.Items;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Content;

/// <summary>
/// Reads and validates the survival content (D16 of K6-29W): <c>items/items.json</c>, <c>terrain/yields.json</c> and <c>start.json</c>,
/// and since K6-31 the optional <c>props/props.json</c> and since K6-32 the optional <c>recipes/recipes.json</c>. Every error is an
/// <see cref="InvalidDataException"/> naming the file and the JSON path. Unknown properties (e.g. <c>visual</c>) are ignored.
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

    /// <summary>Recipes file relative to the content root (K6-32 D4); optional.</summary>
    public const string RecipesFile = "recipes/recipes.json";

    /// <summary>Hazards file relative to the content root (K6-34 D8); optional.</summary>
    public const string HazardsFile = "hazards/hazards.json";

    /// <summary>Most recipes in the file (K6-32 D26).</summary>
    public const int MaxRecipes = 1024;

    /// <summary>Most ingredients of a recipe (K6-32 D26).</summary>
    public const int MaxRecipeIngredients = 8;

    /// <summary>Most fuel entries of a recipe (K6-32 D26).</summary>
    public const int MaxRecipeFuel = 2;

    /// <summary>Longest time of a piece in seconds (K6-32 D26).</summary>
    public const int MaxRecipeSeconds = 3600;

    private const int MaxStackLimit = 999;

    /// <summary>Largest extra maximum health or stamina of a food, in points (K6-33 D8).</summary>
    private const int MaxFoodPoints = 500;

    /// <summary>Largest extra health regeneration per second of a food (K6-33 D8).</summary>
    private const double MaxFoodRegen = 50;

    /// <summary>Shortest duration of a food in seconds (K6-33 D8).</summary>
    private const int MinFoodSeconds = 30;

    /// <summary>Longest duration of a food in seconds (K6-33 D8).</summary>
    private const int MaxFoodSeconds = 7200;
    private const int MaxPropHitPoints = 1000;
    private const int MaxPropYieldEntries = 8;

    /// <summary>The longest regrow time of a prop: 30 days in seconds (K6-31 review of B1).</summary>
    private const int MaxRegrowSeconds = 2_592_000;

    // Stands for an unpaired surrogate in the text: such a character has no UTF-8 form, so the JSON parser would refuse the whole file without a path.
    private const char InvalidCharacter = '￿';

    private static readonly JsonDocumentOptions _options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = false };

    /// <summary>Reads the files from a content root directory; <see cref="PropsFile"/> and <see cref="RecipesFile"/> only when they exist.</summary>
    /// <param name="contentRoot">Directory with <see cref="ItemsFile"/>, <see cref="YieldsFile"/>, <see cref="StartFile"/> and optionally <see cref="PropsFile"/> and <see cref="RecipesFile"/>.</param>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidDataException">The content is invalid.</exception>
    public static SurvivalContent LoadDirectory(string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        var items = File.ReadAllText(Path.Combine(contentRoot, ItemsFile));
        var yields = File.ReadAllText(Path.Combine(contentRoot, YieldsFile));
        var start = File.ReadAllText(Path.Combine(contentRoot, StartFile));
        var propsPath = Path.Combine(contentRoot, PropsFile);
        var recipesPath = Path.Combine(contentRoot, RecipesFile);
        var hazardsPath = Path.Combine(contentRoot, HazardsFile);
        return ParseAll(
            items,
            yields,
            start,
            File.Exists(propsPath) ? File.ReadAllText(propsPath) : null,
            File.Exists(recipesPath) ? File.ReadAllText(recipesPath) : null,
            File.Exists(hazardsPath) ? File.ReadAllText(hazardsPath) : null);
    }

    /// <summary>Parses and validates the content.</summary>
    /// <param name="itemsJson">Text of <see cref="ItemsFile"/>.</param>
    /// <param name="yieldsJson">Text of <see cref="YieldsFile"/>.</param>
    /// <param name="startJson">Text of <see cref="StartFile"/>.</param>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidDataException">The content is invalid.</exception>
    public static SurvivalContent Parse(string itemsJson, string yieldsJson, string startJson) => ParseAll(itemsJson, yieldsJson, startJson, null, null);

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
        return ParseAll(itemsJson, yieldsJson, startJson, propsJson, null);
    }

    /// <summary>Parses and validates the content with the props of <see cref="PropsFile"/> (K6-31 D8) and the recipes of <see cref="RecipesFile"/> (K6-32 D4).</summary>
    /// <param name="itemsJson">Text of <see cref="ItemsFile"/>.</param>
    /// <param name="yieldsJson">Text of <see cref="YieldsFile"/>.</param>
    /// <param name="startJson">Text of <see cref="StartFile"/>.</param>
    /// <param name="propsJson">Text of <see cref="PropsFile"/>, or null when the content has no props.</param>
    /// <param name="recipesJson">Text of <see cref="RecipesFile"/>, or null when the content has no recipes.</param>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidDataException">The content is invalid.</exception>
    public static SurvivalContent Parse(string itemsJson, string yieldsJson, string startJson, string? propsJson, string? recipesJson)
        => ParseAll(itemsJson, yieldsJson, startJson, propsJson, recipesJson);

    /// <summary>Parses and validates the content with the props, the recipes and the hazards of <see cref="HazardsFile"/> (K6-34 D8).</summary>
    /// <param name="itemsJson">Text of <see cref="ItemsFile"/>.</param>
    /// <param name="yieldsJson">Text of <see cref="YieldsFile"/>.</param>
    /// <param name="startJson">Text of <see cref="StartFile"/>.</param>
    /// <param name="propsJson">Text of <see cref="PropsFile"/>, or null when the content has no props.</param>
    /// <param name="recipesJson">Text of <see cref="RecipesFile"/>, or null when the content has no recipes.</param>
    /// <param name="hazardsJson">Text of <see cref="HazardsFile"/>, or null when the content has no hazards.</param>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidDataException">The content is invalid.</exception>
    public static SurvivalContent Parse(string itemsJson, string yieldsJson, string startJson, string? propsJson, string? recipesJson, string? hazardsJson)
        => ParseAll(itemsJson, yieldsJson, startJson, propsJson, recipesJson, hazardsJson);

    private static SurvivalContent ParseAll(string itemsJson, string yieldsJson, string startJson, string? propsJson, string? recipesJson, string? hazardsJson = null)
    {
        if (hazardsJson is not null)
        {
            throw new NotImplementedException("K6-34 B3");
        }

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

        PropDef[] props = [];
        if (propsJson is not null)
        {
            using var document = Open(propsJson, PropsFile);
            props = ParseProps(document.RootElement, catalog);
        }

        RecipeDef[] recipes = [];
        if (recipesJson is not null)
        {
            using var document = Open(recipesJson, RecipesFile);
            recipes = ParseRecipes(document.RootElement, catalog);
        }

        for (var i = 0; i < items.Length; i++)
        {
            var key = items[i].Key;
            if ((items[i].Flags & ItemFlags.Retired) == 0 && !HasSource(key, yields, startKit, props, recipes))
            {
                throw Invalid(ItemsFile, $"$.items[{i}]", $"item '{key}' has no source (a terrain yield, a prop, the start kit or a recipe)");
            }
        }

        return new SurvivalContent(catalog, yields, startKit, props, recipes);
    }

    private static bool HasSource(string key, TerrainYieldDef[] yields, StartKitDef startKit, PropDef[] props, RecipeDef[] recipes)
    {
        foreach (var recipe in recipes)
        {
            if (recipe.Result.ItemKey == key)
            {
                return true;
            }
        }

        foreach (var prop in props)
        {
            foreach (var entry in prop.Yield)
            {
                if (entry.ItemKey == key)
                {
                    return true;
                }
            }
        }

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

            var food = ParseFood(element, path, flags);
            items.Add(new ItemDef(key, category, maxStack, flags, (byte)toolTier, slot, food));
            index++;
        }

        if (items.Count > ProtocolConstants.MaxItemTypes)
        {
            throw Invalid(ItemsFile, "$.items", $"more than {ProtocolConstants.MaxItemTypes} items");
        }

        return [.. items];
    }

    // The "food" object of an item (K6-33 D8): present exactly when the flag Food is, never on a retired item; four required fields.
    private static FoodDef? ParseFood(JsonElement item, string path, ItemFlags flags)
    {
        var hasObject = item.TryGetProperty("food", out var food);
        var hasFlag = (flags & ItemFlags.Food) != 0;
        var foodPath = path + ".food";
        if (hasFlag != hasObject)
        {
            throw Invalid(ItemsFile, foodPath, hasFlag ? "an item with the flag 'food' needs a \"food\" object" : "a \"food\" object needs the flag 'food'");
        }

        if (!hasObject)
        {
            return null;
        }

        if ((flags & ItemFlags.Retired) != 0)
        {
            throw Invalid(ItemsFile, foodPath, "a retired item cannot be food");
        }

        RequireObject(food, foodPath, ItemsFile);
        var health = RequiredInt(food, "health", foodPath, ItemsFile, 0, MaxFoodPoints);
        var stamina = RequiredInt(food, "stamina", foodPath, ItemsFile, 0, MaxFoodPoints);
        if (!food.TryGetProperty("regen", out var regenElement) || regenElement.ValueKind != JsonValueKind.Number
            || !regenElement.TryGetDouble(out var regen) || !double.IsFinite(regen) || regen < 0 || regen > MaxFoodRegen)
        {
            throw Invalid(ItemsFile, foodPath + ".regen", $"must be a finite number in 0..{MaxFoodRegen}");
        }

        var seconds = RequiredInt(food, "durationSeconds", foodPath, ItemsFile, MinFoodSeconds, MaxFoodSeconds);
        if (health == 0 && stamina == 0)
        {
            throw Invalid(ItemsFile, foodPath, "health and stamina cannot both be 0");
        }

        return new FoodDef((ushort)health, (ushort)stamina, (float)regen, (uint)seconds);
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

    private static PropDef[] ParseProps(JsonElement root, ItemCatalog catalog)
    {
        RequireObject(root, "$", PropsFile);
        if (RequiredInt(root, "formatVersion", "$", PropsFile, 1, 1) != 1)
        {
            throw Invalid(PropsFile, "$.formatVersion", "unsupported version");
        }

        var array = RequiredArray(root, "props", "$", PropsFile);
        var props = new List<PropDef>();
        var kinds = new HashSet<PropKindV1>();
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var path = $"$.props[{index}]";
            RequireObject(element, path, PropsFile);
            var key = RequiredKey(element, "key", path, PropsFile);
            if (!PropKeys.TryGetKind(key, out var kind))
            {
                throw Invalid(PropsFile, path + ".key", $"unknown prop key '{key}'");
            }

            if (!kinds.Add(kind))
            {
                throw Invalid(PropsFile, path + ".key", $"duplicate key '{key}'");
            }

            var hitPoints = RequiredInt(element, "hitPoints", path, PropsFile, 1, MaxPropHitPoints);
            var toolTier = RequiredInt(element, "toolTier", path, PropsFile, 0, byte.MaxValue);
            var regrowSeconds = RequiredInt(element, "regrowSeconds", path, PropsFile, 0, MaxRegrowSeconds);
            var yieldArray = RequiredArray(element, "yield", path, PropsFile);
            var yield = new List<PropYield>();
            var yieldIndex = 0;
            foreach (var entry in yieldArray.EnumerateArray())
            {
                var entryPath = $"{path}.yield[{yieldIndex}]";
                if (yieldIndex >= MaxPropYieldEntries)
                {
                    throw Invalid(PropsFile, path + ".yield", $"at most {MaxPropYieldEntries} entries");
                }

                RequireObject(entry, entryPath, PropsFile);
                var item = RequiredKey(entry, "item", entryPath, PropsFile);
                if (!catalog.TryGet(item, out _))
                {
                    throw Invalid(PropsFile, entryPath + ".item", $"unknown item '{item}'");
                }

                var count = RequiredInt(entry, "count", entryPath, PropsFile, 1, MaxStackLimit);
                yield.Add(new PropYield(item, (ushort)count));
                yieldIndex++;
            }

            if (yield.Count == 0)
            {
                throw Invalid(PropsFile, path + ".yield", $"expected 1..{MaxPropYieldEntries} entries");
            }

            props.Add(new PropDef(key, (ushort)hitPoints, (byte)toolTier, yield, (uint)regrowSeconds));
            index++;
        }

        foreach (var kind in Enum.GetValues<PropKindV1>())
        {
            if (kind != PropKindV1.None && !kinds.Contains(kind))
            {
                throw Invalid(PropsFile, "$.props", $"missing prop '{PropKeys.KeyOf(kind)}'");
            }
        }

        return [.. props];
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

    private static RecipeDef[] ParseRecipes(JsonElement root, ItemCatalog catalog)
    {
        RequireObject(root, "$", RecipesFile);
        if (!root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
        {
            throw Invalid(RecipesFile, "$.formatVersion", "must be 1");
        }

        var array = RequiredArray(root, "recipes", "$", RecipesFile);
        if (array.GetArrayLength() > MaxRecipes)
        {
            throw Invalid(RecipesFile, "$.recipes", $"more than {MaxRecipes} recipes");
        }

        var recipes = new List<RecipeDef>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var path = $"$.recipes[{index}]";
            RequireObject(element, path, RecipesFile);
            var key = RequiredKey(element, "key", path, RecipesFile);
            if (!keys.Add(key))
            {
                throw Invalid(RecipesFile, path + ".key", $"duplicate key '{key}'");
            }

            var station = RequiredEnum<StationKind>(element, "station", path, RecipesFile);
            var level = RequiredInt(element, "level", path, RecipesFile, 1, StationKinds.MaxLevel(station));
            var timed = StationKinds.IsTimed(station);
            var seconds = 0;
            if (timed)
            {
                seconds = RequiredInt(element, "timeSeconds", path, RecipesFile, 1, MaxRecipeSeconds);
            }
            else if (element.TryGetProperty("timeSeconds", out _))
            {
                throw Invalid(RecipesFile, path + ".timeSeconds", "only a station with time has a time per piece");
            }

            var ingredients = ParseEntries(element, "ingredients", path, catalog, 1, MaxRecipeIngredients, required: true, []);
            var ingredientKeys = new HashSet<string>(ingredients.Select(i => i.ItemKey), StringComparer.Ordinal);
            if (!timed && element.TryGetProperty("fuel", out var fuelElement) && !(fuelElement.ValueKind == JsonValueKind.Array && fuelElement.GetArrayLength() == 0))
            {
                throw Invalid(RecipesFile, path + ".fuel", "only a station with time uses fuel");
            }

            var fuel = timed ? ParseEntries(element, "fuel", path, catalog, 0, MaxRecipeFuel, required: false, ingredientKeys) : [];
            var result = ParseResult(element, path, catalog);
            string? progressKey = null;
            if (element.TryGetProperty("progressKey", out var keyElement) && keyElement.ValueKind != JsonValueKind.Null)
            {
                progressKey = RequiredKey(element, "progressKey", path, RecipesFile);
            }

            recipes.Add(new RecipeDef(key, station, (byte)level, ingredients, fuel, result, (uint)seconds, progressKey));
            index++;
        }

        return [.. recipes];
    }

    private static RecipeEntry[] ParseEntries(
        JsonElement recipe, string name, string path, ItemCatalog catalog, int min, int max, bool required, HashSet<string> forbidden)
    {
        if (!required && !recipe.TryGetProperty(name, out _))
        {
            return [];
        }

        var array = RequiredArray(recipe, name, path, RecipesFile);
        var count = array.GetArrayLength();
        if (count < min || count > max)
        {
            throw Invalid(RecipesFile, $"{path}.{name}", $"must have {min}..{max} entries");
        }

        var entries = new RecipeEntry[count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var entryPath = $"{path}.{name}[{index}]";
            RequireObject(element, entryPath, RecipesFile);
            var def = RequiredItem(element, entryPath, catalog);
            if (!seen.Add(def.Key))
            {
                throw Invalid(RecipesFile, entryPath + ".item", $"item '{def.Key}' repeats in this list");
            }

            if (forbidden.Contains(def.Key))
            {
                throw Invalid(RecipesFile, entryPath + ".item", $"item '{def.Key}' is also an ingredient");
            }

            entries[index] = new RecipeEntry(def.Key, (ushort)RequiredInt(element, "count", entryPath, RecipesFile, 1, MaxStackLimit));
            index++;
        }

        return entries;
    }

    private static RecipeEntry ParseResult(JsonElement recipe, string path, ItemCatalog catalog)
    {
        var resultPath = path + ".result";
        if (!recipe.TryGetProperty("result", out var element))
        {
            throw Invalid(RecipesFile, resultPath, "expected an object");
        }

        RequireObject(element, resultPath, RecipesFile);
        var def = RequiredItem(element, resultPath, catalog);
        return new RecipeEntry(def.Key, (ushort)RequiredInt(element, "count", resultPath, RecipesFile, 1, def.MaxStack));
    }

    // An item of a recipe is known and not retired (spec 03, "Obsługa błędów").
    private static ItemDef RequiredItem(JsonElement entry, string path, ItemCatalog catalog)
    {
        var key = RequiredKey(entry, "item", path, RecipesFile);
        if (!catalog.TryGet(key, out var def))
        {
            throw Invalid(RecipesFile, path + ".item", $"unknown item '{key}'");
        }

        return (def.Flags & ItemFlags.Retired) != 0 ? throw Invalid(RecipesFile, path + ".item", $"item '{key}' is retired") : def;
    }

    private static JsonDocument Open(string json, string file)
    {
        try
        {
            return JsonDocument.Parse(MarkUnpairedSurrogates(json), _options);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{file}: invalid JSON: {e.Message}", e);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException($"{file}: invalid JSON: {e.Message}", e);
        }
    }

    private static string MarkUnpairedSurrogates(string text)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var paired = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            if (paired)
            {
                builder?.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                builder ??= new StringBuilder(text, 0, i, text.Length);
                builder.Append(InvalidCharacter);
            }
            else
            {
                builder?.Append(c);
            }
        }

        return builder?.ToString() ?? text;
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
        if (text.Length == 0 || text.Contains(InvalidCharacter, StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(text) > ProtocolConstants.MaxRegistryKeyBytes)
        {
            throw Invalid(file, $"{path}.{name}", $"a key has 1..{ProtocolConstants.MaxRegistryKeyBytes} UTF-8 bytes and no unpaired surrogate");
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
