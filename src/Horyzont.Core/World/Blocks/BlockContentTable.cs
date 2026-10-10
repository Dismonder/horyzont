namespace Horyzont.Core.World.Blocks;

/// <summary>
/// Block definitions of a world by <c>TypeId</c>, built once when the world starts (the registry is fixed from then on).
/// Used by intent validators and ship calculations, never by the operation applier.
/// </summary>
public sealed class BlockContentTable
{
    /// <summary>Mass of the placeholder definition, kg.</summary>
    public const float MissingMassKg = 100f;

    private const byte MissingMaterialId = 1;

    private readonly BlockTypeDef[] _byId;
    private readonly BlockTypeDef _missing;

    private BlockContentTable(BlockTypeDef[] byId, BlockTypeDef missing)
    {
        _byId = byId;
        _missing = missing;
    }

    /// <summary>A table without definitions: every type is the placeholder.</summary>
    public static BlockContentTable Empty { get; } = new([], CreateMissing());

    /// <summary>Builds the table.</summary>
    /// <param name="registry">Block types of the world.</param>
    /// <param name="definitions">Content definitions by key; a registered key absent here gets the placeholder.</param>
    /// <returns>The table.</returns>
    public static BlockContentTable Create(BlockTypeRegistry registry, IReadOnlyDictionary<string, BlockTypeDef> definitions)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(definitions);
        var missing = definitions.TryGetValue(BlockTypeRegistry.MissingKey, out var content) ? content : CreateMissing();
        var entries = registry.Entries;
        var maxId = 0;
        foreach (var entry in entries)
        {
            maxId = Math.Max(maxId, entry.Id);
        }

        var byId = new BlockTypeDef[maxId + 1];
        Array.Fill(byId, missing);
        foreach (var entry in entries)
        {
            if (definitions.TryGetValue(entry.Key, out var def))
            {
                byId[entry.Id] = def;
            }
        }

        return new BlockContentTable(byId, missing);
    }

    /// <summary>Definition of a type; the placeholder for 0 and unknown ids.</summary>
    /// <param name="typeId">Block type id.</param>
    /// <returns>The definition.</returns>
    public BlockTypeDef Get(ushort typeId) => typeId < _byId.Length ? _byId[typeId] : _missing;

    private static BlockTypeDef CreateMissing() =>
        new(BlockTypeRegistry.MissingKey, BlockShape.Cube, MissingMaterialId, true, MissingMassKg, BlockFunction.None);
}
