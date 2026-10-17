using Horyzont.Constructs.Content;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Constructs.Meshing;

/// <summary>Immutable lookup typeId → render info for one world session; safe to read from any thread.</summary>
public sealed class BlockRenderTable
{
    private readonly BlockRenderInfo[] _infos;
    private readonly BlockRenderInfo _missing;

    private BlockRenderTable(BlockRenderInfo[] infos, BlockRenderInfo missing, string[] unknownKeys)
    {
        _infos = infos;
        _missing = missing;
        UnknownKeys = unknownKeys;
    }

    /// <summary>Number of registry keys the content does not know (drawn as core:missing); the caller logs them once.</summary>
    public int MissingKeyCount => UnknownKeys.Count;

    /// <summary>Registry keys the content does not know, in registry order.</summary>
    public IReadOnlyList<string> UnknownKeys { get; }

    /// <summary>Builds the table for a world registry.</summary>
    /// <param name="registry">Pairs id–key of the world (from the registry or <c>ServerHello.BlockTypes</c>).</param>
    /// <param name="catalog">Game content.</param>
    /// <returns>The table.</returns>
    public static BlockRenderTable Build(IReadOnlyList<RegistryEntry> registry, BlockTypeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        BuiltInShapeMeshes.TryResolve(BuiltInShapeMeshes.DoorOpenRef, out var doorOpen);
        var missing = From(catalog.Missing, true, doorOpen);
        var maxId = 0;
        foreach (var entry in registry)
        {
            maxId = Math.Max(maxId, entry.Id);
        }

        var infos = new BlockRenderInfo[maxId + 1];
        Array.Fill(infos, missing);
        infos[0] = default;
        var unknown = new List<string>();
        foreach (var entry in registry)
        {
            if (entry.Id == 0)
            {
                continue;
            }

            if (entry.Key == BlockTypeRegistry.MissingKey)
            {
                infos[entry.Id] = missing;
            }
            else if (catalog.TryGet(entry.Key, out var content))
            {
                infos[entry.Id] = From(content, false, doorOpen);
            }
            else
            {
                infos[entry.Id] = missing;
                unknown.Add(entry.Key);
            }
        }

        return new BlockRenderTable(infos, missing, [.. unknown]);
    }

    /// <summary>Render info of core:missing (also used for malformed cells).</summary>
    public ref readonly BlockRenderInfo Missing => ref _missing;

    /// <summary>Render info of a type: 0 is empty; an unknown or out-of-range id is core:missing.</summary>
    /// <param name="typeId">Block type id.</param>
    /// <returns>The info.</returns>
    public ref readonly BlockRenderInfo Get(ushort typeId)
    {
        if (typeId < _infos.Length)
        {
            return ref _infos[typeId];
        }

        return ref _missing;
    }

    private static BlockRenderInfo From(BlockTypeContent content, bool isMissing, ShapeMesh? doorOpen) =>
        new(
            content.Def.Shape, content.Def.MaterialId, content.Def.HasCollision, isMissing, content.Mesh, content.Def.Function.Kind,
            content.Def.Function.Kind == BlockFunctionKind.Door ? doorOpen : null, content.Light);
}
