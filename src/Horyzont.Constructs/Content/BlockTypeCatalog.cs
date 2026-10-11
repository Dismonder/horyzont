using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Horyzont.Core.Protocol;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Constructs.Content;

/// <summary>Block types and visual materials of the game content (not saved; rebuilt at every start).</summary>
public sealed partial class BlockTypeCatalog
{
    /// <summary>Version of the <c>blocks.json</c> format.</summary>
    public const int FormatVersion = 2;

    /// <summary>Id of the built-in material of <c>core:missing</c> (layer 0 of the block textures).</summary>
    public const byte MissingMaterialId = 1;

    /// <summary>Key of the built-in material of <c>core:missing</c>.</summary>
    public const string MissingMaterialKey = "core:missing";

    private const uint MissingColor = 0xFF00FFFFu;

    private readonly Dictionary<string, BlockTypeContent> _byKey;

    private BlockTypeCatalog(
        string[] keys, BlockTypeContent[] blocks, BlockMaterialDef[] materials, BlockTypeContent missing, Dictionary<string, BlockTypeContent> byKey)
    {
        BlockTypeKeys = keys;
        Blocks = blocks;
        Materials = materials;
        Missing = missing;
        _byKey = byKey;
    }

    /// <summary>Keys in content order without core:missing; pass to WorldState.CreateNew / WorldLoadOptions.BlockTypeKeys.</summary>
    public IReadOnlyList<string> BlockTypeKeys { get; }

    /// <summary>Block types in content order, then core:missing.</summary>
    public IReadOnlyList<BlockTypeContent> Blocks { get; }

    /// <summary>Materials by id from 1: the built-in missing material first, then the content materials in content order.</summary>
    public IReadOnlyList<BlockMaterialDef> Materials { get; }

    /// <summary>The built-in core:missing type.</summary>
    public BlockTypeContent Missing { get; }

    /// <summary>Looks up a block type by key.</summary>
    /// <param name="key">Registry key.</param>
    /// <param name="block">The type when known.</param>
    /// <returns>True when the content has the key (core:missing included).</returns>
    public bool TryGet(string key, [NotNullWhen(true)] out BlockTypeContent? block) => _byKey.TryGetValue(key, out block);

    /// <summary>Parses blocks.json. Throws <see cref="InvalidDataException"/> with a JSON path on any invalid entry.</summary>
    /// <param name="utf8Json">File content.</param>
    /// <param name="meshes">Resolver for mesh references other than the built-in ones.</param>
    /// <returns>The catalog.</returns>
    public static BlockTypeCatalog Parse(ReadOnlySpan<byte> utf8Json, IShapeMeshResolver meshes)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"$: invalid JSON ({e.Message})", e);
        }

        using (document)
        {
            var root = document.RootElement;
            Require(root.ValueKind == JsonValueKind.Object, "$", "must be an object");
            var version = ReadInt(root, "$", "formatVersion");
            Require(version == FormatVersion, "$.formatVersion", $"must be {FormatVersion}");

            var materials = ParseMaterials(root);
            var materialIds = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (var material in materials)
            {
                materialIds[material.Key] = material.Id;
            }

            var blocksElement = RequireArray(root, "$", "blocks");
            Require(blocksElement.GetArrayLength() + 1 <= ProtocolConstants.MaxBlockTypes, "$.blocks", $"at most {ProtocolConstants.MaxBlockTypes - 1} block types");

            var blocks = new List<BlockTypeContent>();
            var keys = new List<string>();
            var byKey = new Dictionary<string, BlockTypeContent>(StringComparer.Ordinal);
            var index = 0;
            foreach (var element in blocksElement.EnumerateArray())
            {
                var path = $"$.blocks[{index++}]";
                Require(element.ValueKind == JsonValueKind.Object, path, "must be an object");
                var content = ParseBlock(element, path, materialIds, meshes);
                Require(byKey.TryAdd(content.Def.Key, content), $"{path}.key", $"duplicate key '{content.Def.Key}'");
                blocks.Add(content);
                keys.Add(content.Def.Key);
            }

            var missing = new BlockTypeContent(
                new BlockTypeDef(BlockTypeRegistry.MissingKey, BlockShape.Cube, MissingMaterialId, true, BlockContentTable.MissingMassKg, BlockFunction.None),
                "BLOCK_MISSING", null);
            byKey.Add(missing.Def.Key, missing);
            blocks.Add(missing);
            return new BlockTypeCatalog([.. keys], [.. blocks], [.. materials], missing, byKey);
        }
    }

    private static List<BlockMaterialDef> ParseMaterials(JsonElement root)
    {
        var array = RequireArray(root, "$", "materials");
        Require(array.GetArrayLength() + 1 <= ProtocolConstants.MaxMaterials, "$.materials", $"at most {ProtocolConstants.MaxMaterials - 1} materials");
        var materials = new List<BlockMaterialDef> { new(MissingMaterialId, MissingMaterialKey, MissingColor) };
        var seen = new HashSet<string>(StringComparer.Ordinal) { MissingMaterialKey };
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var path = $"$.materials[{index++}]";
            Require(element.ValueKind == JsonValueKind.Object, path, "must be an object");
            var key = ReadString(element, path, "key");
            Require(MaterialKeyRegex().IsMatch(key), $"{path}.key", "must match ^[a-z0-9_]+$");
            Require(seen.Add(key), $"{path}.key", $"duplicate material '{key}'");
            var color = ParseColor(ReadString(element, path, "color"), $"{path}.color");
            materials.Add(new BlockMaterialDef((byte)(materials.Count + 1), key, color));
        }

        return materials;
    }

    private static BlockTypeContent ParseBlock(JsonElement element, string path, Dictionary<string, byte> materialIds, IShapeMeshResolver meshes)
    {
        var key = ReadString(element, path, "key");
        Require(BlockKeyRegex().IsMatch(key), $"{path}.key", "must match ^[a-z0-9_]+:[a-z0-9_/]+$");
        Require(key != BlockTypeRegistry.MissingKey, $"{path}.key", "core:missing is built in and must not be listed");
        Require(Encoding.UTF8.GetByteCount(key) <= ProtocolConstants.MaxRegistryKeyBytes, $"{path}.key", $"at most {ProtocolConstants.MaxRegistryKeyBytes} bytes");

        var shapeText = ReadString(element, path, "shape");
        var shape = shapeText switch
        {
            "cube" => BlockShape.Cube,
            "mesh" => BlockShape.Mesh,
            _ => throw Invalid($"{path}.shape", "must be 'cube' or 'mesh'"),
        };

        var materialKey = ReadString(element, path, "material");
        Require(materialIds.TryGetValue(materialKey, out var materialId) && materialKey != MissingMaterialKey, $"{path}.material", $"unknown material '{materialKey}'");

        var collision = true;
        if (element.TryGetProperty("collision", out var collisionElement))
        {
            Require(collisionElement.ValueKind is JsonValueKind.True or JsonValueKind.False, $"{path}.collision", "must be a boolean");
            collision = collisionElement.GetBoolean();
        }

        ShapeMesh? mesh = null;
        var hasMesh = element.TryGetProperty("mesh", out var meshElement);
        if (shape == BlockShape.Mesh)
        {
            Require(hasMesh, $"{path}.mesh", "is required for shape 'mesh'");
            Require(meshElement.ValueKind == JsonValueKind.String, $"{path}.mesh", "must be a string");
            var meshRef = meshElement.GetString()!;
            Require(BuiltInShapeMeshes.TryResolve(meshRef, out mesh) || meshes.TryResolve(meshRef, out mesh), $"{path}.mesh", $"cannot resolve '{meshRef}'");
            var problem = mesh!.Validate();
            Require(problem is null, $"{path}.mesh", $"invalid mesh '{meshRef}': {problem}");
        }
        else
        {
            Require(!hasMesh, $"{path}.mesh", "is only allowed for shape 'mesh'");
        }

        var mass = ReadPositiveFloat(element, path, "mass");
        var function = element.TryGetProperty("function", out var functionElement)
            ? ParseFunction(functionElement, $"{path}.function")
            : BlockFunction.None;

        var local = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var nameKey = "BLOCK_" + local.Replace('/', '_').ToUpperInvariant();
        return new BlockTypeContent(new BlockTypeDef(key, shape, materialId, collision, mass, function), nameKey, mesh);
    }

    private static uint ParseColor(string text, string path)
    {
        Require(ColorRegex().IsMatch(text), path, "must be #RRGGBB or #RRGGBBAA");
        var value = uint.Parse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return text.Length == 7 ? (value << 8) | 0xFFu : value;
    }

    private static BlockFunction ParseFunction(JsonElement element, string path)
    {
        Require(element.ValueKind == JsonValueKind.Object, path, "must be an object");
        var kind = ReadString(element, path, "kind");
        BlockFunction function;
        string[] allowed;
        switch (kind)
        {
            case "cockpit":
                function = BlockFunction.Cockpit;
                allowed = [];
                break;
            case "seat":
                var role = ReadString(element, path, "role");
                function = BlockFunction.CreateSeat(role switch
                {
                    "passenger" => SeatRole.Passenger,
                    "navigator" => SeatRole.Navigator,
                    _ => throw Invalid($"{path}.role", "must be 'passenger' or 'navigator'"),
                });
                allowed = ["role"];
                break;
            case "engine":
                function = BlockFunction.CreateEngine(ReadPositiveFloat(element, path, "thrust"), ReadTier(element, path));
                allowed = ["thrust", "tier"];
                break;
            case "stabilizer":
                function = BlockFunction.CreateStabilizer(ReadPositiveFloat(element, path, "torque"));
                allowed = ["torque"];
                break;
            case "fuel_tank":
                function = BlockFunction.CreateFuelTank(ReadPositiveFloat(element, path, "capacity"));
                allowed = ["capacity"];
                break;
            case "cargo":
                function = BlockFunction.CreateCargo(ReadTier(element, path));
                allowed = ["tier"];
                break;
            case "landing_gear":
                function = BlockFunction.LandingGear;
                allowed = [];
                break;
            case "pulse_drive":
                function = BlockFunction.CreatePulseDrive(ReadPositiveFloat(element, path, "speedCap"));
                allowed = ["speedCap"];
                break;
            case "door":
                function = BlockFunction.Door;
                allowed = [];
                break;
            case "refuel_station":
                function = BlockFunction.RefuelStation;
                allowed = [];
                break;
            default:
                throw Invalid($"{path}.kind", $"unknown function kind '{kind}'");
        }

        foreach (var property in element.EnumerateObject())
        {
            Require(property.Name == "kind" || Array.IndexOf(allowed, property.Name) >= 0, $"{path}.{property.Name}", $"is not a parameter of '{kind}'");
        }

        return function;
    }

    private static byte ReadTier(JsonElement parent, string path)
    {
        Require(parent.TryGetProperty("tier", out var value), $"{path}.tier", "is required");
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var tier) && tier is >= 1 and <= 255, $"{path}.tier", "must be an integer 1..255");
        return (byte)value.GetInt32();
    }

    private static float ReadPositiveFloat(JsonElement parent, string path, string name)
    {
        Require(parent.TryGetProperty(name, out var value), $"{path}.{name}", "is required");
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number) && float.IsFinite(number) && number > 0f, $"{path}.{name}", "must be a positive finite number");
        return value.GetSingle();
    }

    private static JsonElement RequireArray(JsonElement parent, string path, string name)
    {
        Require(parent.TryGetProperty(name, out var value), $"{path}.{name}", "is required");
        Require(value.ValueKind == JsonValueKind.Array, $"{path}.{name}", "must be an array");
        return value;
    }

    private static string ReadString(JsonElement parent, string path, string name)
    {
        Require(parent.TryGetProperty(name, out var value), $"{path}.{name}", "is required");
        Require(value.ValueKind == JsonValueKind.String, $"{path}.{name}", "must be a string");
        return value.GetString()!;
    }

    private static int ReadInt(JsonElement parent, string path, string name)
    {
        Require(parent.TryGetProperty(name, out var value), $"{path}.{name}", "is required");
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _), $"{path}.{name}", "must be an integer");
        return value.GetInt32();
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string path, string message)
    {
        if (!condition)
        {
            throw Invalid(path, message);
        }
    }

    private static InvalidDataException Invalid(string path, string message) => new($"{path}: {message}");

    [GeneratedRegex("^[a-z0-9_]+:[a-z0-9_/]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex BlockKeyRegex();

    [GeneratedRegex("^[a-z0-9_]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex MaterialKeyRegex();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();
}
